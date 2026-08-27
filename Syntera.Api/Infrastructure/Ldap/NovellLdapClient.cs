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

    public async Task<LdapAuthResult> AuthenticateAsync(
        LdapEndpoint endpoint, string email, string password, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Email must be sanitized for LDAP filter safety (RFC 4515).
            var escaped = EscapeLdapFilter(email);

            using var conn = CreateConnection(endpoint);
            await conn.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);

            // Optional StartTLS upgrade on plain 389.
            if (endpoint.UseStartTls && endpoint.Port != 636)
            {
                await conn.StartTlsAsync().ConfigureAwait(false);
            }

            // Direct bind: user's own email + password.
            // Active Directory accepts userPrincipalName (email format) as bind identity.
            try
            {
                await conn.BindAsync(email, password).ConfigureAwait(false);
                if (!conn.Bound)
                {
                    return new LdapAuthResult(false, null, email, null,
                        "Invalid credentials (LDAP bind failed).",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (LdapException ex)
            {
                // 49 = LDAP_INVALID_CREDENTIALS (wrong password / unknown user)
                return new LdapAuthResult(false, null, email, null,
                    ex.ResultCode == 49 ? "Invalid email or password." : $"LDAP bind failed: {ex.Message}",
                    (int)sw.Elapsed.TotalMilliseconds);
            }

            // Bind succeeded — search for the user's own entry to fetch details.
            // We use the bound connection (user's own credentials) for the search.
            var filter = BuildUserFilter(escaped);
            var searchResults = await conn.SearchAsync(
                endpoint.BaseDn, LdapConnection.ScopeSub, filter,
                new[] { AttrEmail, AttrDisplayName, AttrAccountControl, AttrMail }, false, ct).ConfigureAwait(false);

            LdapEntry? userEntry = null;
            await foreach (var entry in searchResults.WithCancellation(ct).ConfigureAwait(false))
            {
                if (userEntry is not null)
                {
                    // Ambiguous — multiple entries match. Refuse to authenticate.
                    return new LdapAuthResult(false, null, email, null,
                        $"Multiple LDAP entries match email '{email}'. Refusing to authenticate.",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
                userEntry = entry;
            }

            if (userEntry is null)
            {
                // Bind worked but search returned nothing — unusual. Treat as auth failure.
                return new LdapAuthResult(false, null, email, null,
                    "Bind succeeded but user entry not found in directory.",
                    (int)sw.Elapsed.TotalMilliseconds);
            }

            // Extract display name and account status.
            var attrSet = userEntry.GetAttributeSet();
            string? displayName = attrSet.ContainsKey(AttrDisplayName)
                ? attrSet[AttrDisplayName].StringValue
                : null;
            string? mail = attrSet.ContainsKey(AttrMail)
                ? attrSet[AttrMail].StringValue
                : email;

            if (attrSet.ContainsKey(AttrAccountControl))
            {
                var uacStr = attrSet[AttrAccountControl].StringValue;
                if (int.TryParse(uacStr, out var uac) && (uac & UF_ACCOUNTDISABLE) != 0)
                {
                    return new LdapAuthResult(false, userEntry.Dn, mail, displayName,
                        "LDAP account is disabled (userAccountControl has ACCOUNTDISABLE bit).",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
            }

            displayName ??= email;
            return new LdapAuthResult(true, userEntry.Dn, mail, displayName, null,
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (LdapException ex)
        {
            return new LdapAuthResult(false, null, null, null,
                $"LDAP server error: {ex.Message} (code={ex.ResultCode})",
                (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            return new LdapAuthResult(false, null, null, null,
                $"LDAP connection failed: {ex.Message}",
                (int)sw.Elapsed.TotalMilliseconds);
        }
    }

    public Task<LdapAuthResult> TestConnectionAsync(
        LdapEndpoint endpoint, string testEmail, string testPassword, CancellationToken ct = default)
        => AuthenticateAsync(endpoint, testEmail, testPassword, ct);

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
                    if (c < 0x20) sb.Append('\\').Append(((int)c).ToString("x2"));
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
        options.ConfigureRemoteCertificateValidationCallback(
            (sender, certificate, chain, sslPolicyErrors) => true);

        var conn = new LdapConnection(options)
        {
            ConnectionTimeout = DefaultTimeoutMs,
        };
        return conn;
    }
}
