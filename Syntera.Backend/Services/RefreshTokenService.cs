using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;

/// <summary>
/// Refresh-token lifecycle extracted from the former AuthService god-class
/// (1.700-line refactor). Owns everything about the opaque refresh token
/// itself: generation (random + HMAC signature), verification, hashing,
/// row construction, family revocation, session limits (§11.300(d)), TTL
/// per scope, and the token-pair issuance helper used by all login/refresh
/// flows.
///
/// <para>Token format: <c>{base64url random}.{base64url hmac}</c>. The random
/// part carries the entropy; the HMAC (key derived from Jwt:SigningKey via
/// subdomain separation) enables fast-reject of forged tokens without a DB
/// lookup (L3, DoS mitigation). The DB stores SHA-256(random part) — never
/// the raw token (P2 fix).</para>
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Generate a fresh signed refresh token (random.HMAC format).</summary>
    string Generate(int bytes = 32);

    /// <summary>Verify the HMAC signature (constant-time). False on malformed input.</summary>
    bool VerifySignature(string? token);

    /// <summary>SHA-256 hex of the token's random part — the DB lookup key.</summary>
    string HashToken(string token);

    /// <summary>Build the RefreshToken persistence row for a freshly generated raw token.</summary>
    RefreshToken BuildToken(Guid userId, string scope, Guid? siteId, string rawToken, string? ip, string? ua, Guid? familyId = null);

    /// <summary>Revoke every non-revoked token sharing the family (M1 theft response).</summary>
    Task RevokeFamilyAsync(DbSet<RefreshToken> tokens, Guid? familyId, Guid revokedBy, CancellationToken ct);

    /// <summary>
    /// COMPLIANCE (Sprint 2.5): idle timeout + max session age per 21 CFR Part 11
    /// §11.300(d). Throws AuthenticationException (SESSION_IDLE_TIMEOUT /
    /// SESSION_EXPIRED) and revokes the token when a limit is exceeded.
    /// </summary>
    Task EnforceSessionLimitsAsync(RefreshToken token, DbContext db, string? ip, string? ua, CancellationToken ct);

    /// <summary>Issue a full token pair (JWT access + fresh signed refresh).</summary>
    Task<(string AccessToken, DateTime ExpiresAt, string RefreshToken)> IssueTokensAsync(
        Guid userId, string scope, Guid? siteId, string email, string displayName, string? title,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions,
        long version, string? ip, string? ua, CancellationToken ct);

    /// <summary>M2: refresh TTL per scope (platform 1d / site 7d defaults).</summary>
    TimeSpan GetTtl(string scope);
}

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly ITokenService _tokens;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly IAuditService _audit;

    // L3: HMAC key for refresh token signature (fast-reject invalid tokens
    // without DB lookup). Derived from Jwt:SigningKey so no extra config.
    private readonly byte[] _refreshHmacKey;

    public RefreshTokenService(
        ITokenService tokens,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IAuditService audit)
    {
        _tokens = tokens;
        _config = config;
        _audit = audit;

        // L3: derive HMAC key from JWT signing key. Same secret, different
        // purpose (subdomain separation via "refresh-token-v1" prefix).
        var jwtKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required for refresh token HMAC (L3).");
        // Hash the (prefix + jwtKey) string with SHA-256 to derive a 32-byte
        // HMAC key. Subdomain separation via the prefix ensures this key is
        // distinct from any other use of Jwt:SigningKey.
        _refreshHmacKey = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("syntera:refresh-hmac:v1:" + jwtKey));
    }

    /// <summary>
    /// SECURITY (L3): generate a refresh token that is both random AND
    /// HMAC-signed. Format: <c>{base64url random}.{base64url hmac}</c>.
    /// The random part has the entropy; the HMAC lets the server reject
    /// obviously-forged tokens without a DB lookup (DoS mitigation — an
    /// attacker spraying random strings at /api/auth/refresh would
    /// otherwise force a DB query per attempt).
    /// </summary>
    public string Generate(int bytes = 32)
    {
        var buf = new byte[bytes];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(buf);
        var random = Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // HMAC-SHA256 over the random part. Verification: server recomputes
        // HMAC on incoming token, constant-time compares. If mismatch → reject
        // before DB query.
        using var hmac = new System.Security.Cryptography.HMACSHA256(_refreshHmacKey);
        var sig = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(random));
        var sigB64 = Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{random}.{sigB64}";
    }

    /// <summary>
    /// SECURITY (L3): verify the HMAC signature on an incoming refresh token.
    /// Returns false (without throwing) if the token is malformed or the
    /// signature doesn't match — caller should treat as REFRESH_NOT_FOUND,
    /// not as a different error (don't leak that the signature check failed).
    /// </summary>
    public bool VerifySignature(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1) return false;
        var random = token[..dot];
        var sigB64 = token[(dot + 1)..];

        byte[] expectedSig;
        try
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(_refreshHmacKey);
            expectedSig = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(random));
        }
        catch
        {
            return false;
        }

        // Decode incoming signature (base64url → bytes). Malformed = reject.
        byte[]? incomingSig;
        try
        {
            // base64url → base64
            var b64 = sigB64.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            incomingSig = Convert.FromBase64String(b64);
        }
        catch
        {
            return false;
        }

        // Constant-time comparison to avoid timing-based signature oracle.
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            expectedSig, incomingSig);
    }

    /// <summary>
    /// SECURITY (L3): the SHA-256 hash is computed over the random portion of
    /// the token (the signature suffix is server-derived and adds no entropy).
    /// </summary>
    public string HashToken(string token)
        => SHA256Hex(TokenRandomPart(token));

    /// <summary>
    /// Build the persistence row for a raw refresh token.
    ///
    /// <para>FIX (Sprint 2.5 — hash mismatch): hashes ONLY the random part,
    /// matching the verify path (both sides now agree). FIX (P2 — raw token
    /// nullified the hash's purpose): stores an EMPTY string in Token — the
    /// raw value is never needed server-side (all lookups go through
    /// TokenHash) and persisting it would leak live tokens on a DB read.</para>
    /// </summary>
    public RefreshToken BuildToken(Guid userId, string scope, Guid? siteId, string rawToken, string? ip, string? ua, Guid? familyId = null)
    {
        return new RefreshToken
        {
            Token = string.Empty,
            TokenHash = SHA256Hex(TokenRandomPart(rawToken)),
            UserId = userId,
            UserScope = scope,
            SiteId = siteId,
            ExpiresAt = DateTime.UtcNow.Add(GetTtl(scope)),
            CreatedFromIp = ip,
            CreatedUserAgent = ua,
            // M1: new tokens without an explicit FamilyId start a new family.
            // Rotation will propagate the parent's FamilyId via the caller.
            FamilyId = familyId ?? Guid.NewGuid(),
            // COMPLIANCE (Sprint 2.5 fix): set LastUsedAt = UtcNow on creation
            // so the idle-timeout check has a baseline — the timer starts at
            // login, so idle timeout applies uniformly.
            LastUsedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// SECURITY (M1): revoke every non-revoked refresh token that shares the
    /// given family ID. Called when token reuse is detected. Works against
    /// either the Platform DB's RefreshTokens DbSet or a Site DB's
    /// RefreshTokens DbSet (same entity type). CALLER is responsible for
    /// SaveChangesAsync.
    /// </summary>
    public async Task RevokeFamilyAsync(DbSet<RefreshToken> tokens, Guid? familyId, Guid revokedBy, CancellationToken ct)
    {
        if (familyId is null) return;
        var now = DateTime.UtcNow;
        // Only revoke tokens that are still active — already-revoked tokens
        // keep their original RevokedAt/RevokedBy for audit clarity.
        var active = await tokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = now;
            t.RevokedBy = revokedBy;
        }
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.5): idle session timeout + max session age.
    ///
    /// <para><b>Idle timeout:</b> if the refresh token has not been used for
    /// longer than <c>Session:IdleMinutes</c> (default 30 min), the session
    /// is treated as idle-expired and the refresh is rejected with
    /// <c>SESSION_IDLE_TIMEOUT</c>.</para>
    ///
    /// <para><b>Max session age (P1 fix):</b> measured from the OLDEST row in
    /// the token's family (the login row) — not the current token's CreatedAt,
    /// which resets on every rotation. Without the fix, the 8-hour limit could
    /// never fire for an actively-refreshing session.</para>
    /// </summary>
    public async Task EnforceSessionLimitsAsync(RefreshToken token, DbContext db, string? ip, string? ua, CancellationToken ct)
    {
        var idleMinutes = _config.GetValue<int>("Session:IdleMinutes", 30);
        if (idleMinutes > 0 && token.LastUsedAt is not null)
        {
            var idleSince = DateTime.UtcNow - token.LastUsedAt.Value;
            if (idleSince.TotalMinutes > idleMinutes)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedBy = token.UserId;
                await db.SaveChangesAsync(ct);
                await _audit.LogAsync(new AuditEntry(
                    SiteId: token.SiteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.session_idle_timeout", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                    Outcome: "failure", ErrorMessage: $"Idle for {idleSince.TotalMinutes:F0} min (> {idleMinutes} min limit)",
                    SignatureMeaning: "Automatic session logoff after idle period (21 CFR Part 11 §11.300(d))."), ct);
                throw new Models.AuthenticationException("SESSION_IDLE_TIMEOUT",
                    $"Session timed out after {idleMinutes} minutes of inactivity. Please log in again.");
            }
        }

        var maxHours = _config.GetValue<int>("Session:MaxHours", 8);
        if (maxHours > 0)
        {
            // FIX (P1 — dead max-session-age control): the previous check used
            // token.CreatedAt — but every rotation inserts a NEW row with
            // CreatedAt = "now", so the measured "session age" was only ever the
            // age of the CURRENT token. With IdleMinutes=30, the idle check
            // always fired first and the 8-hour maximum could NEVER trigger — a
            // live compliance gap for 21 CFR Part 11 §11.300(d).
            var sessionStart = await GetSessionStartAsync(db, token, ct);
            var sessionAge = DateTime.UtcNow - sessionStart;
            if (sessionAge.TotalHours > maxHours)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedBy = token.UserId;
                await db.SaveChangesAsync(ct);
                await _audit.LogAsync(new AuditEntry(
                    SiteId: token.SiteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.session_expired", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                    Outcome: "failure", ErrorMessage: $"Session age {sessionAge.TotalHours:F1}h > {maxHours}h max",
                    SignatureMeaning: "Automatic session logoff after max session length (21 CFR Part 11 §11.300(d))."), ct);
                throw new Models.AuthenticationException("SESSION_EXPIRED",
                    $"Session exceeded the maximum duration of {maxHours} hours. Please log in again.");
            }
        }
    }

    /// <summary>
    /// Issue a full token pair: JWT access token (via ITokenService) + a fresh
    /// signed refresh token.
    /// </summary>
    public Task<(string AccessToken, DateTime ExpiresAt, string RefreshToken)> IssueTokensAsync(
        Guid userId, string scope, Guid? siteId, string email, string displayName, string? title,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions,
        long version, string? ip, string? ua, CancellationToken ct)
    {
        var access = _tokens.IssueFor(userId, scope, siteId, email, displayName, title, roles, permissions, version);
        var refresh = Generate();
        return Task.FromResult((access.Token, access.ExpiresAt, refresh));
    }

    /// <summary>
    /// M2: refresh token TTL per scope. Platform Admin tokens are
    /// shorter-lived (default 1 day) because they carry the highest
    /// privilege. Site user tokens are longer-lived (default 7 days)
    /// because end users shouldn't be forced to re-login every day,
    /// and site-scoped tokens have a smaller blast radius (one site only).
    /// Falls back to legacy Jwt:RefreshTokenDays if per-scope key is absent.
    /// </summary>
    public TimeSpan GetTtl(string scope)
    {
        var legacy = _config["Jwt:RefreshTokenDays"];
        var perScope = scope == "platform"
            ? _config["Jwt:RefreshTokenDaysPlatform"]
            : _config["Jwt:RefreshTokenDaysSite"];

        // Per-scope key takes precedence; legacy key is the fallback; final
        // fallback is a sane default (1 day for platform, 7 days for site).
        var str = perScope ?? legacy;
        if (int.TryParse(str, out var days) && days > 0)
            return TimeSpan.FromDays(days);
        return scope == "platform" ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
    }

    /// <summary>
    /// FIX (P1): the session's true start time = CreatedAt of the OLDEST row in
    /// the refresh-token family (the row created at login). Pre-feature rows
    /// (FamilyId null) fall back to the token's own CreatedAt. Indexed on
    /// FamilyId — one cheap query per refresh.
    /// </summary>
    private static async Task<DateTime> GetSessionStartAsync(DbContext db, RefreshToken token, CancellationToken ct)
    {
        if (token.FamilyId is null) return token.CreatedAt;
        var earliest = await db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.FamilyId == token.FamilyId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return earliest ?? token.CreatedAt;
    }

    /// <summary>Extract the random portion of a signed refresh token.</summary>
    private static string TokenRandomPart(string token)
    {
        var dot = token.IndexOf('.');
        return dot > 0 ? token[..dot] : token;
    }

    private static string SHA256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
