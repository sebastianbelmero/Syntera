using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// ─── Migration / Operations documentation ────────────────────────────
// COMPLIANCE (Sprint 2.6): Site.DatabaseConnectionString is now encrypted
// at rest using ASP.NET Core Data Protection (DPAPI). This closes the
// ISO 27001 A.8.24 (cryptographic protection of data at rest) gap and
// satisfies 21 CFR Part 11 §11.10(c) (protection and retrieval of
// records) for the per-site SQL Server credentials stored in the
// Platform DB's Sites table.
//
// OPT-IN MECHANISM
//   Encryption is opt-in via "ConnectionProtection:Enabled" in
//   appsettings.json. When false (or missing) the protector acts as a
//   no-op — Protect returns the plaintext as-is and Unprotect returns
//   the value as-is. This lets existing dev environments skip the
//   encryption overhead and lets existing plaintext DBs continue to
//   work without migration.
//
//   When true (default for Production), Protect encrypts new writes and
//   Unprotect handles both ENC:-prefixed (encrypted) and bare (plaintext)
//   values — backward compat. A Production rollout can therefore flip
//   the flag and restart; the DbSeeder auto-migrates existing plaintext
//   rows to encrypted form on the next seeder run (idempotent — never
//   re-encrypts an already-encrypted value).
//
// ENABLING IN PRODUCTION
//   1. Set "ConnectionProtection": { "Enabled": true } in appsettings.json
//      (or via SYNTERA_ConnectionProtection__Enabled=true env var).
//   2. Restart the app. The DbSeeder will detect any site row whose
//      DatabaseConnectionString does not start with "ENC:" and rewrite
//      it as an encrypted value. New sites seeded from config are
//      encrypted on first insert.
//
// KEY RING DEPENDENCY (CRITICAL)
//   This protector uses the same IDataProtectionProvider registered in
//   Program.cs (purpose "Syntera.ConnectionString.v1"). The key ring is
//   persisted to DataProtection:KeyPath (default
//   /var/lib/syntera/keys).
//
//   ⚠️  BACK UP the DataProtection:KeyPath directory. If the key ring is
//   lost, every encrypted connection string becomes undecryptable. The
//   Platform Admin must then re-enter each site's connection string in
//   appsettings.json (plaintext) and restart — the seeder will encrypt
//   them again with the freshly generated key ring. This is the same
//   recovery path used for TOTP secrets (Sprint 2.3).
//
// BACKWARD COMPAT / FALLBACK
//   Unprotect inspects the value: if it starts with "ENC:" it strips
//   the prefix and calls IDataProtector.Unprotect (which handles
//   Base64-decoding internally). Otherwise it returns the value
//   verbatim — so plaintext connection strings from existing DBs
//   continue to resolve correctly. This also means a config value with
//   ConnectionProtection:Enabled=false round-trips cleanly (Protect
//   returns plaintext, Unprotect returns plaintext).
//
// FAILURE MODES
//   - Corrupted ciphertext or key-ring mismatch: Unprotect throws
//     InvalidOperationException with a clear message. Callers should
//     surface this as a 500 to the Platform Admin (the Site cannot be
//     reached until the key ring is restored or the value is re-seeded).
//   - DPAPI disabled (ConnectionProtection:Enabled=false): Protect
//     returns plaintext; Unprotect returns plaintext; IsEncrypted
//     returns false. No exceptions.
//
// PERFORMANCE
//   Protect is only invoked on the write path (DbSeeder at startup, or
//   any future Site-management endpoint that writes the connection
//   string). Unprotect is invoked on every SiteDbContext resolution
//   (once per request that touches a Site DB). The SiteDbContextFactory
//   caches the resolved DbContext per request, so Unprotect runs at
//   most once per request. The DPAPI AES-256 decrypt of a ~340-char
//   ciphertext is sub-millisecond and well under the SQL connection
//   setup cost, so no per-request cache is needed at this time.
namespace Syntera.Backend.Services;

/// <summary>
/// Encrypts / decrypts per-site SQL Server connection strings stored in
/// the Platform DB's <c>Sites.DatabaseConnectionString</c> column using
/// ASP.NET Core Data Protection (DPAPI). See the file-level documentation
/// comment above for the opt-in mechanism, migration path, and key-ring
/// backup requirements.
/// </summary>
public interface IConnectionStringProtector
{
    /// <summary>
    /// Encrypt the supplied plaintext connection string. When
    /// <c>ConnectionProtection:Enabled</c> is false (or missing), returns
    /// the plaintext unchanged. When enabled, returns the ciphertext
    /// Base64-encoded and prefixed with <c>"ENC:"</c> so <see cref="Unprotect"/>
    /// can detect already-encrypted values.
    /// <para>
    /// Idempotent: if the supplied value already starts with
    /// <c>"ENC:"</c>, it is returned verbatim — never re-encrypted.
    /// </para>
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypt a stored connection-string value. If the value is
    /// <c>"ENC:"</c>-prefixed, strips the prefix and unprotects.
    /// Otherwise returns the value verbatim (backward compat with
    /// plaintext DBs and with <c>ConnectionProtection:Enabled=false</c>).
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> on decrypt failure
    /// (corrupted ciphertext or DPAPI key-ring mismatch).
    /// </para>
    /// </summary>
    string Unprotect(string encrypted);

    /// <summary>
    /// Heuristic: returns true if the supplied value appears to be a
    /// Protect-produced ciphertext (starts with <c>"ENC:"</c>). Used by
    /// the DbSeeder to skip re-encryption of already-encrypted rows.
    /// </summary>
    bool IsEncrypted(string value);
}

public sealed class ConnectionStringProtector : IConnectionStringProtector
{
    private readonly IDataProtector _protector;
    private readonly IConfiguration _config;
    private readonly ILogger<ConnectionStringProtector>? _logger;

    /// <summary>
    /// Prefix tag prepended to every ciphertext produced by
    /// <see cref="Protect"/>. <see cref="Unprotect"/> uses this to
    /// distinguish encrypted from plaintext values, so existing plaintext
    /// DBs continue to work without migration. The character <c>':'</c>
    /// is outside the Base64 alphabet, so no legitimate Base64 ciphertext
    /// can start with <c>"ENC:"</c> — the sentinel is unambiguous.
    /// </summary>
    private const string EncryptedPrefix = "ENC:";

    /// <summary>
    /// DPAPI purpose string. Namespacing the protector means a key
    /// compromise of another protector (e.g. the TOTP protector at
    /// <c>"Syntera.Totp.v1"</c>) cannot be used to forge connection
    /// strings, and vice versa. The <c>.v1</c> suffix allows future
    /// key-rotation schemes to bump the version without colliding with
    /// legacy ciphertext.
    /// </summary>
    public const string Purpose = "Syntera.ConnectionString.v1";

    public ConnectionStringProtector(
        IDataProtectionProvider protectionProvider,
        IConfiguration config,
        ILogger<ConnectionStringProtector>? logger = null)
    {
        // IDataProtector is thread-safe — the protector can be registered
        // as a Singleton. We register as Scoped to mirror the spec, which
        // also makes it easier to inject request-scoped dependencies in
        // the future (e.g. a per-request decrypt cache).
        _protector = protectionProvider.CreateProtector(Purpose);
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// True when <c>ConnectionProtection:Enabled</c> is set in config.
    /// Missing or false → no-op (dev / backward-compat default). Read on
    /// every call (rather than cached at construction) so a config
    /// reload via <c>IConfiguration.AddJsonFile(reloadOnChange:true)</c>
    /// flips encryption on without an app restart.
    /// </summary>
    private bool IsEnabled =>
        _config.GetValue<bool?>("ConnectionProtection:Enabled") ?? false;

    /// <inheritdoc/>
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (!IsEnabled) return plaintext;

        // Idempotency guard: never double-encrypt. If the caller hands us
        // a value that is already encrypted (e.g. seeder re-runs against
        // a value that was already migrated), return it verbatim —
        // re-encrypting would produce a different ciphertext (DPAPI uses
        // a random IV) but the same plaintext, which would bump UpdatedAt
        // on every seeder run for no semantic reason.
        if (IsEncrypted(plaintext)) return plaintext;

        // The string overload of IDataProtector.Protect handles UTF-8 +
        // Base64 encoding internally — the returned string is a
        // Base64 representation of (version | keyId | IV | ciphertext | HMAC).
        // We prepend "ENC:" so Unprotect can detect already-encrypted
        // values and skip the no-op decrypt attempt.
        var cipher = _protector.Protect(plaintext);
        return EncryptedPrefix + cipher;
    }

    /// <inheritdoc/>
    public string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return encrypted;

        // Backward compat: plaintext values (no ENC: prefix) are returned
        // verbatim. This covers existing DBs that were never migrated,
        // AND dev environments where ConnectionProtection:Enabled=false
        // (in which case Protect also returns plaintext — no ENC: prefix
        // is ever produced, so this branch is the common one in dev).
        //
        // Note: Unprotect does NOT consult IsEnabled. A value with the
        // ENC: prefix is ALWAYS decrypted — even when ConnectionProtection:
        // Enabled=false. This is intentional: if an operator flips the
        // flag from true to false (e.g. for debugging), the stored
        // ciphertext must still resolve to the plaintext connection
        // string. Otherwise the SiteDbContext would receive the "ENC:..."
        // literal and fail to connect with a confusing SQL error. The
        // IsEnabled flag only gates Protect (new writes) — Unprotect
        // must always honor the ENC: sentinel to keep the read path
        // robust against config changes.
        if (!IsEncrypted(encrypted)) return encrypted;

        var b64 = encrypted[EncryptedPrefix.Length..];
        try
        {
            // The string overload of Unprotect handles the Base64 decode
            // + key-id lookup + AES decrypt + HMAC verify internally.
            return _protector.Unprotect(b64);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to decrypt a connection string via ASP.NET Core Data Protection " +
                "(purpose {Purpose}). The DPAPI key ring may have been lost or the value " +
                "was encrypted by a different key ring.",
                Purpose);

            throw new InvalidOperationException(
                $"Failed to decrypt a connection string via ASP.NET Core Data Protection " +
                $"(purpose \"{Purpose}\"). The DPAPI key ring at DataProtection:KeyPath " +
                $"may have been lost, or the value was encrypted by a different key ring. " +
                $"Restore the key ring, or re-seed the site's connection string via " +
                $"appsettings.json and restart.",
                ex);
        }
    }

    /// <inheritdoc/>
    public bool IsEncrypted(string value)
        => !string.IsNullOrEmpty(value)
           && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
}
