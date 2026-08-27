using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace Syntera.Infrastructure.Ldap;

/// <summary>
/// Result of an LDAP authentication attempt. Either <see cref="IsSuccess"/>
/// is true (with user details populated) or <see cref="ErrorMessage"/> explains
/// the failure. Never throws on LDAP protocol errors — the caller decides.
/// </summary>
public record LdapAuthResult(
    bool IsSuccess,
    string? Dn,
    string? Email,
    string? DisplayName,
    string? ErrorMessage,
    int LatencyMs);

public record LdapUserEntry(
    string Dn,
    string Email,
    string DisplayName,
    bool IsActive);

/// <summary>
/// Thin abstraction over <c>Novell.Directory.Ldap</c>. Uses <b>direct bind</b>:
/// the user's own email + password is used to authenticate. No service account.
///
/// This matches the reference JS implementation (ldap-get-user.js) the
/// customer provided. After a successful bind, we search for the user's
/// own entry (using their bound credentials) to fetch displayName and
/// userAccountControl.
/// </summary>
public interface ILdapClient
{
    /// <summary>
    /// Authenticate user by email + password via direct bind.
    /// 1. Connect to LDAP server
    /// 2. (Optional) StartTLS
    /// 3. Bind with email + password
    /// 4. On success, search user details (displayName, userAccountControl)
    /// </summary>
    Task<LdapAuthResult> AuthenticateAsync(LdapEndpoint endpoint, string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Test connection — same as AuthenticateAsync, but used by Platform Admin
    /// "Test LDAP" button to verify a real user can log in.
    /// </summary>
    Task<LdapAuthResult> TestConnectionAsync(LdapEndpoint endpoint, string testEmail, string testPassword, CancellationToken ct = default);
}

public record LdapEndpoint(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn);

/// <summary>
/// Novell-based implementation. Plain LDAP on port 389 is allowed
/// (password transmitted cleartext — operator accepts the risk).
/// </summary>
public sealed class NovellLdapClient : ILdapClient
{
    private const string AttrEmail = "userPrincipalName";
    private const string AttrDisplayName = "displayName";
    private const string AttrAccountControl = "userAccountControl";
    private const string AttrMail = "mail";
    private const int UF_ACCOUNTDISABLE = 0x0002;
    private const int LdapV3 = 3;
    private const int DefaultTimeoutMs = 20_000;

    private readonly ILogger<NovellLdapClient>? _logger;

    public NovellLdapClient(ILogger<NovellLdapClient>? logger = null)
    {
        _logger = logger;
    }

    public async Task<LdapAuthResult> AuthenticateAsync(
        LdapEndpoint endpoint, string email, string password, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LogInfo("LDAP auth starting for {Email} via {Host}:{Port} (StartTLS={StartTLS}, BaseDn={BaseDn})",
            email, endpoint.Host, endpoint.Port, endpoint.UseStartTls, endpoint.BaseDn);

        try
        {
            // Email must be sanitized for LDAP filter safety (RFC 4515).
            var escaped = EscapeLdapFilter(email);

            using var conn = CreateConnection(endpoint);
            LogInfo("Connecting to {Host}:{Port}...", endpoint.Host, endpoint.Port);
            await conn.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
            LogInfo("Connected to {Host}:{Port}.", endpoint.Host, endpoint.Port);

            // Optional StartTLS upgrade on plain 389.
            if (endpoint.UseStartTls && endpoint.Port != 636)
            {
                LogInfo("Starting TLS upgrade...");
                await conn.StartTlsAsync(CancellationToken.None).ConfigureAwait(false);
                LogInfo("TLS established.");
            }

            // Direct bind: user's own email + password.
            // Active Directory accepts userPrincipalName (email format) as bind identity.
            LogInfo("Binding as {Email}...", email);
            try
            {
                await conn.BindAsync(email, password, CancellationToken.None).ConfigureAwait(false);
                LogInfo("Bind result: Bound={Bound}", conn.Bound);
                if (!conn.Bound)
                {
                    return Fail(email, "Invalid email or password (bind returned false).", sw);
                }
            }
            catch (LdapException ex)
            {
                LogError(ex, "Bind failed. ResultCode={Code} ({CodeName})", ex.ResultCode, ex.Message);
                // 49 = LDAP_INVALID_CREDENTIALS (wrong password / unknown user)
                if (ex.ResultCode == 49)
                    return Fail(email, "Invalid email or password.", sw);
                return Fail(email, $"LDAP bind failed (code {ex.ResultCode}): {ex.Message}", sw);
            }

            // Bind succeeded — search for the user's own entry to fetch details.
            // We use the bound connection (user's own credentials) for the search.
            var filter = BuildUserFilter(escaped);
            LogInfo("Searching for user. BaseDn={BaseDn}, Filter={Filter}", endpoint.BaseDn, filter);

            var searchResults = await conn.SearchAsync(
                endpoint.BaseDn, LdapConnection.ScopeSub, filter,
                new[] { AttrEmail, AttrDisplayName, AttrAccountControl, AttrMail }, false, ct).ConfigureAwait(false);

            LdapEntry? userEntry = null;
            int matchCount = 0;
            await foreach (var entry in searchResults.WithCancellation(ct).ConfigureAwait(false))
            {
                matchCount++;
                LogInfo("Search returned entry: {Dn}", entry.Dn);
                if (userEntry is not null)
                {
                    // Ambiguous — multiple entries match. Refuse to authenticate.
                    return Fail(email, $"Multiple LDAP entries match email '{email}'. Refusing to authenticate.", sw);
                }
                userEntry = entry;
            }

            if (userEntry is null)
            {
                LogError("Bind succeeded but search returned 0 matches. Filter={Filter}", filter);
                return Fail(email, "Bind succeeded but user entry not found in directory. Check BaseDn and filter.", sw);
            }

            // Extract display name and account status.
            var attrSet = userEntry.GetAttributeSet();
            string? displayName = attrSet.TryGetValue(AttrDisplayName, out var dnAttr)
                ? dnAttr.StringValue
                : null;
            string? mail = attrSet.TryGetValue(AttrMail, out var mailAttr)
                ? mailAttr.StringValue
                : email;

            if (attrSet.TryGetValue(AttrAccountControl, out var uacAttribute))
            {
                var uacStr = uacAttribute.StringValue;
                LogInfo("userAccountControl={Uac}", uacStr);
                if (int.TryParse(uacStr, out var uac) && (uac & UF_ACCOUNTDISABLE) != 0)
                {
                    return Fail(email, "LDAP account is disabled (userAccountControl has ACCOUNTDISABLE bit).", sw);
                }
            }

            displayName ??= email;
            LogInfo("LDAP auth SUCCESS: {Email} → {Dn} ({Name})", email, userEntry.Dn, displayName);
            return new LdapAuthResult(true, userEntry.Dn, mail, displayName, null,
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (LdapException ex)
        {
            LogError(ex, "LDAP server error: {Message} (code={Code})", ex.Message, ex.ResultCode);
            return new LdapAuthResult(false, null, null, null,
                $"LDAP server error: {ex.Message} (code={ex.ResultCode})",
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            LogError(ex, "LDAP connection failed: {Message}", ex.Message);
            return new LdapAuthResult(false, null, null, null,
                $"LDAP connection failed: {ex.Message}",
                (int)sw.Elapsed.TotalMilliseconds);
        }
    }

    public Task<LdapAuthResult> TestConnectionAsync(
        LdapEndpoint endpoint, string testEmail, string testPassword, CancellationToken ct = default)
        => AuthenticateAsync(endpoint, testEmail, testPassword, ct);

    private LdapAuthResult Fail(string email, string message, System.Diagnostics.Stopwatch sw)
    {
        LogWarning("LDAP auth FAILED for {Email}: {Message}", email, message);
        return new LdapAuthResult(false, null, email, null, message, (int)sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Escape characters that have special meaning in LDAP filter syntax (RFC 4515).
    /// </summary>
    private static string EscapeLdapFilter(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length + 16);
        foreach (var c in input)
        {
            switch (c)
            {
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\\': sb.Append("\\5c"); break;
                case '\0': sb.Append("\\00"); break;
                default:
                    if (c < 0x20) sb.Append('\\').Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Build a user-search filter that matches multiple AD attributes:
    /// userPrincipalName, mail, sAMAccountName, displayName, cn, proxyAddresses.
    /// Mirrors the reference JS implementation's buildUserFilter().
    /// </summary>
    private static string BuildUserFilter(string escapedEmail)
    {
        return $"(&" +
               $"(objectCategory=person)" +
               $"(objectClass=user)" +
               $"(|" +
               $"(userPrincipalName={escapedEmail})" +
               $"(mail={escapedEmail})" +
               $"(sAMAccountName={escapedEmail})" +
               $"(displayName={escapedEmail})" +
               $"(cn={escapedEmail})" +
               $"(proxyAddresses=smtp:{escapedEmail})" +
               $"(proxyAddresses=SMTP:{escapedEmail})" +
               $")" +
               $")";
    }

    private static LdapConnection CreateConnection(LdapEndpoint endpoint)
    {
        var options = new LdapConnectionOptions();
        if (endpoint.Port == 636) options.UseSsl();

        // Accept self-signed certificates — operator accepts the risk for internal AD.
        // In production with proper CA trust, remove this callback.
#pragma warning disable CA5359 // Do not disable certificate validation
        options.ConfigureRemoteCertificateValidationCallback(
            (sender, certificate, chain, sslPolicyErrors) => true);
#pragma warning restore CA5359

        var conn = new LdapConnection(options)
        {
            ConnectionTimeout = DefaultTimeoutMs,
        };
        return conn;
    }

    // Logging helpers. CA1848/CA2254 suppressed because LDAP diagnostic
    // messages are intentionally dynamic (interpolated filter strings, etc.)
    // and the performance impact is negligible for this low-frequency path.
#pragma warning disable CA1848 // Use LoggerMessage delegates
#pragma warning disable CA2254 // Template should be a static expression
    private void LogInfo(string msg, params object[] args) => _logger?.LogInformation(msg, args);
    private void LogWarning(string msg, params object[] args) => _logger?.LogWarning(msg, args);
    private void LogError(string msg, params object[] args) => _logger?.LogError(msg, args);
    private void LogError(Exception ex, string msg, params object[] args) => _logger?.LogError(ex, msg, args);
#pragma warning restore CA2254
#pragma warning restore CA1848
}

