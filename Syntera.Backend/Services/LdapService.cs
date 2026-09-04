using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace Syntera.Backend.Services;

/// <summary>
/// Result of an LDAP authentication attempt. Either <see cref="IsSuccess"/>
/// is true (with user details populated) or <see cref="ErrorMessage"/> explains
/// the failure. Never throws on LDAP protocol errors — the caller decides.
///
/// <para><b>DisplayName</b> and <b>Title</b> are <c>null</c> when AD does not return
/// them (referral, search error, attribute missing). The caller MUST NOT fall back
/// to <c>Email</c> — that would overwrite the database's existing value during
/// auto-sync on every login. Leave them null and let the caller keep the DB value.</para>
/// </summary>
public record LdapAuthResult(
    bool IsSuccess,
    string? Dn,
    string? Email,
    string? DisplayName,
    string? Title,
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
    string BaseDn,
    string? UpnDomain);

/// <summary>
/// Novell-based implementation. Plain LDAP on port 389 is allowed
/// (password transmitted cleartext — operator accepts the risk).
/// </summary>
public sealed class NovellLdapClient : ILdapClient
{
    private const string AttrEmail = "userPrincipalName";
    private const string AttrDisplayName = "displayName";
    private const string AttrTitle = "title";
    private const string AttrAccountControl = "userAccountControl";
    private const string AttrMail = "mail";
    private const int UF_ACCOUNTDISABLE = 0x0002;
    private const int LdapV3 = 3;
    private const int DefaultTimeoutMs = 20_000;

    private readonly ILogger<NovellLdapClient>? _logger;
    private readonly IConfiguration? _configuration;

    public NovellLdapClient(
        IConfiguration? configuration = null,
        ILogger<NovellLdapClient>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LdapAuthResult> AuthenticateAsync(
        LdapEndpoint endpoint, string email, string password, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Transform email → bind DN using UPN domain if configured.
        // e.g., email="user@kalventis.com", UpnDomain="kalventis.dom"
        //       → bindDn="user@kalventis.dom"
        // This is necessary because AD's userPrincipalName uses the AD
        // domain suffix (.dom), which often differs from the email domain (.com).
        var bindDn = email;
        if (!string.IsNullOrWhiteSpace(endpoint.UpnDomain))
        {
            var atIndex = email.IndexOf('@');
            if (atIndex > 0)
            {
                bindDn = email[..atIndex] + "@" + endpoint.UpnDomain;
            }
            else
            {
                // No @ in the input — treat the whole thing as username.
                bindDn = email + "@" + endpoint.UpnDomain;
            }
        }

        LogInfo("LDAP auth starting for {Email} (bind as {BindDn}) via {Host}:{Port} (StartTLS={StartTLS}, BaseDn={BaseDn})",
            email, bindDn, endpoint.Host, endpoint.Port, endpoint.UseStartTls, endpoint.BaseDn);

        try
        {
            // Email must be sanitized for LDAP filter safety (RFC 4515).
            var escaped = EscapeLdapFilter(email);

            using var conn = CreateConnection(endpoint, skipCertValidation: ShouldSkipCertValidation());
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
            LogInfo("Binding as {BindDn}...", bindDn);
            try
            {
                await conn.BindAsync(bindDn, password, CancellationToken.None).ConfigureAwait(false);
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
            // Note: AD often returns Referral (code=10) for cross-domain entries.
            // ldapsearch auto-follows referrals by default; we do the same.
            var filter = BuildUserFilter(escaped);
            LogInfo("Searching for user. BaseDn={BaseDn}, Filter={Filter}", endpoint.BaseDn, filter);

            LdapEntry? userEntry = null;
            int matchCount = 0;

            try
            {
                // Use LdapSearchConstraints with ReferralFollowing = true so
                // referrals are auto-followed (matching ldapsearch default behavior).
                var constraints = new LdapSearchConstraints
                {
                    ReferralFollowing = true,
                };

                var searchResults = await conn.SearchAsync(
                    endpoint.BaseDn, LdapConnection.ScopeSub, filter,
                    new[] { AttrEmail, AttrDisplayName, AttrTitle, AttrAccountControl, AttrMail },
                    false, constraints, ct).ConfigureAwait(false);

                await foreach (var entry in searchResults.WithCancellation(ct).ConfigureAwait(false))
                {
                    matchCount++;
                    LogInfo("Search returned entry: {Dn}", entry.Dn);
                    if (userEntry is not null)
                    {
                        return Fail(email, $"Multiple LDAP entries match '{email}'. Refusing to authenticate.", sw);
                    }
                    userEntry = entry;
                }
            }
            catch (LdapException ex) when (ex.ResultCode == 10)
            {
                // Referral even after auto-follow. Bind already succeeded,
                // so the user IS authenticated — we just couldn't fetch details.
                // IMPORTANT: return null (not email) for DisplayName/Title so the
                // caller's auto-sync logic does NOT overwrite existing DB values.
                LogWarning("Search returned referral (code=10) but bind succeeded. Authenticated; details unavailable.");
                return new LdapAuthResult(true, null, email, null, null, null, (int)sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception searchEx)
            {
                // Any other search failure — bind already succeeded, so treat
                // as authenticated with minimal info. Better UX than failing.
                LogWarning("Search failed after successful bind: {Error}. Authenticated; details unavailable.", searchEx.Message);
                return new LdapAuthResult(true, null, email, null, null, null, (int)sw.Elapsed.TotalMilliseconds);
            }

            if (userEntry is null)
            {
                // Bind worked but search returned 0 entries. User is authenticated
                // (bind proved credentials), just no details available.
                LogWarning("Bind succeeded but search returned 0 matches. Authenticated; details unavailable.");
                return new LdapAuthResult(true, null, email, null, null, null, (int)sw.Elapsed.TotalMilliseconds);
            }

            // Extract display name, mail, and title from the user's entry.
            var attrSet = userEntry.GetAttributeSet();
            string? displayName = attrSet.TryGetValue(AttrDisplayName, out var dnAttr)
                ? dnAttr.StringValue
                : null;
            string? title = attrSet.TryGetValue(AttrTitle, out var titleAttr)
                ? titleAttr.StringValue
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

            // Trim whitespace; treat empty as null so the caller's auto-sync
            // logic does not overwrite an existing DB value with an empty string.
            displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

            LogInfo("LDAP auth SUCCESS: {Email} → {Dn} ({Name}, title={Title})", email, userEntry.Dn, displayName ?? "<none>", title ?? "<none>");
            return new LdapAuthResult(true, userEntry.Dn, mail, displayName, title, null,
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (LdapException ex)
        {
            LogError(ex, "LDAP server error: {Message} (code={Code})", ex.Message, ex.ResultCode);
            return new LdapAuthResult(false, null, null, null, null,
                $"LDAP server error: {ex.Message} (code={ex.ResultCode})",
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            LogError(ex, "LDAP connection failed: {Message}", ex.Message);
            return new LdapAuthResult(false, null, null, null, null,
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
        return new LdapAuthResult(false, null, email, null, null, message, (int)sw.Elapsed.TotalMilliseconds);
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

    /// <summary>
    /// Decide whether to skip LDAP server certificate validation. Returns true when
    /// config key "Ldap:SkipCertValidation" is set (appsettings or env var override via
    /// SYNTERA_Ldap__SkipCertValidation) OR when a debugger is attached.
    /// </summary>
    private bool ShouldSkipCertValidation()
    {
        var skipFromConfig = _configuration?.GetValue<bool>("Ldap:SkipCertValidation") ?? false;
        return skipFromConfig || System.Diagnostics.Debugger.IsAttached;
    }

    private static LdapConnection CreateConnection(LdapEndpoint endpoint, bool skipCertValidation)
    {
        var options = new LdapConnectionOptions();
        if (endpoint.Port == 636) options.UseSsl();

        // Skip certificate validation only when explicitly requested via config
        // (appsettings or env var SYNTERA_Ldap__SkipCertValidation) or debugger attached.
        //
        // In Production with a real CA-signed cert: leave both off and let the OS
        // trust store validate. To allow self-signed in Production, set
        // SYNTERA_Ldap__SkipCertValidation=true (NOT recommended — install internal
        // CA cert to trust store instead).

        if (skipCertValidation)
        {
#pragma warning disable CA5359 // Do not disable certificate validation
            options.ConfigureRemoteCertificateValidationCallback(
                (sender, certificate, chain, sslPolicyErrors) => true);
#pragma warning restore CA5359
        }

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

