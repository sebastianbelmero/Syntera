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
/// Thin abstraction over <c>Novell.Directory.Ldap</c> so that:
/// 1. LDAP protocol details stay in one place (replaceable if we ever
///    switch to System.DirectoryServices.Protocols or a cloud IdP).
/// 2. Unit tests can mock LDAP without standing up a real server.
/// </summary>
public interface ILdapClient
{
    Task<LdapAuthResult> AuthenticateAsync(LdapEndpoint endpoint, LdapCredentials creds, string email, string password, CancellationToken ct = default);
    IAsyncEnumerable<LdapUserEntry> SearchUsersAsync(LdapEndpoint endpoint, LdapCredentials creds, CancellationToken ct = default);
    Task<LdapAuthResult> TestConnectionAsync(LdapEndpoint endpoint, LdapCredentials creds, string testEmail, CancellationToken ct = default);
}

public record LdapEndpoint(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string EmailAttribute,
    string UserFilterTemplate,
    int TimeoutSeconds,
    bool SearchSubtree);

public record LdapCredentials(string? BindDn, string? BindPassword);

/// <summary>
/// Default Novell-based implementation. Connection is always over LDAPS (port 636)
/// OR StartTLS (port 389). Plain 389 without TLS is REJECTED at the caller level.
/// </summary>
public sealed class NovellLdapClient : ILdapClient
{
    private const string AttrEmail = "userPrincipalName";
    private const string AttrDisplayName = "displayName";
    private const string AttrAccountControl = "userAccountControl";
    private const int UF_ACCOUNTDISABLE = 0x0002;

    public async Task<LdapAuthResult> AuthenticateAsync(
        LdapEndpoint endpoint, LdapCredentials creds, string email, string password, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var escaped = EscapeLdapFilter(email);

            using var conn = CreateConnection(endpoint);
            await conn.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);

            // StartTLS if applicable.
            if (endpoint.UseStartTls && endpoint.Port != 636)
            {
                await conn.StartTlsAsync().ConfigureAwait(false);
            }

            // Bind with service account (if configured) or anonymous to search for the user DN.
            if (!string.IsNullOrEmpty(creds.BindDn) && !string.IsNullOrEmpty(creds.BindPassword))
                await conn.BindAsync(creds.BindDn, creds.BindPassword).ConfigureAwait(false);
            else
                await conn.BindAsync(string.Empty, string.Empty).ConfigureAwait(false); // anonymous

            // Search for the user by email.
            var filter = BuildUserFilter(endpoint, escaped);
            var scope = endpoint.SearchSubtree ? LdapConnection.ScopeSub : LdapConnection.ScopeOne;

            var searchResults = await conn.SearchAsync(
                endpoint.BaseDn, scope, filter,
                new[] { AttrEmail, AttrDisplayName, AttrAccountControl }, false, ct).ConfigureAwait(false);

            LdapEntry? userEntry = null;
            await foreach (var entry in searchResults.WithCancellation(ct).ConfigureAwait(false))
            {
                if (userEntry is not null)
                {
                    return new LdapAuthResult(false, null, null, null,
                        $"Multiple LDAP entries match email '{email}' — ambiguous, refusing to authenticate.",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
                userEntry = entry;
            }

            if (userEntry is null)
            {
                return new LdapAuthResult(false, null, null, null,
                    $"No LDAP entry found for email '{email}'.",
                    (int)sw.Elapsed.TotalMilliseconds);
            }

            // Check account is active.
            var attrSet = userEntry.GetAttributeSet();
            string? displayName = null;
            if (attrSet.ContainsKey(AttrDisplayName))
                displayName = attrSet[AttrDisplayName].StringValue;

            if (attrSet.ContainsKey(AttrAccountControl))
            {
                var uacStr = attrSet[AttrAccountControl].StringValue;
                if (int.TryParse(uacStr, out var uac) && (uac & UF_ACCOUNTDISABLE) != 0)
                {
                    return new LdapAuthResult(false, userEntry.Dn, email, displayName,
                        "LDAP account is disabled (userAccountControl has ACCOUNTDISABLE bit).",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
            }

            displayName ??= email;

            // Perform the actual user bind.
            try
            {
                await conn.BindAsync(userEntry.Dn, password).ConfigureAwait(false);
                if (!conn.Bound)
                {
                    return new LdapAuthResult(false, userEntry.Dn, email, displayName,
                        "Invalid credentials (LDAP bind failed).",
                        (int)sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (LdapException ex)
            {
                return new LdapAuthResult(false, userEntry.Dn, email, displayName,
                        $"Invalid credentials: {ex.Message}",
                        (int)sw.Elapsed.TotalMilliseconds);
            }

            return new LdapAuthResult(true, userEntry.Dn, email, displayName, null,
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

    public async IAsyncEnumerable<LdapUserEntry> SearchUsersAsync(
        LdapEndpoint endpoint, LdapCredentials creds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var conn = CreateConnection(endpoint);
        await conn.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
        if (endpoint.UseStartTls && endpoint.Port != 636)
            await conn.StartTlsAsync().ConfigureAwait(false);

        if (!string.IsNullOrEmpty(creds.BindDn) && !string.IsNullOrEmpty(creds.BindPassword))
            await conn.BindAsync(creds.BindDn, creds.BindPassword).ConfigureAwait(false);
        else
            throw new InvalidOperationException("LDAP sync requires a service account (BindDn + BindPassword).");

        var filter = "(&(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";
        var scope = endpoint.SearchSubtree ? LdapConnection.ScopeSub : LdapConnection.ScopeOne;
        var searchResults = await conn.SearchAsync(
            endpoint.BaseDn, scope, filter,
            new[] { AttrEmail, AttrDisplayName, AttrAccountControl }, false, ct).ConfigureAwait(false);

        await foreach (var entry in searchResults.WithCancellation(ct).ConfigureAwait(false))
        {
            var attrSet = entry.GetAttributeSet();
            if (!attrSet.ContainsKey(AttrEmail)) continue;
            var email = attrSet[AttrEmail].StringValue;
            if (string.IsNullOrWhiteSpace(email)) continue;

            var displayName = attrSet.ContainsKey(AttrDisplayName)
                ? attrSet[AttrDisplayName].StringValue
                : email;

            bool isActive = true;
            if (attrSet.ContainsKey(AttrAccountControl))
            {
                var uacStr = attrSet[AttrAccountControl].StringValue;
                if (int.TryParse(uacStr, out var uac))
                    isActive = (uac & UF_ACCOUNTDISABLE) == 0;
            }

            yield return new LdapUserEntry(entry.Dn, email, displayName, isActive);
        }
    }

    public Task<LdapAuthResult> TestConnectionAsync(
        LdapEndpoint endpoint, LdapCredentials creds, string testEmail, CancellationToken ct = default)
        => AuthenticateAsync(endpoint, creds, testEmail, "TEST_INVALID_PASSWORD_SHOULD_FAIL_BIND", ct);

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

    private static string BuildUserFilter(LdapEndpoint endpoint, string escapedEmail)
    {
        var tpl = string.IsNullOrWhiteSpace(endpoint.UserFilterTemplate)
            ? "(&(objectClass=user)({emailAttribute}={email}))"
            : endpoint.UserFilterTemplate;
        return tpl
            .Replace("{emailAttribute}", endpoint.EmailAttribute)
            .Replace("{email}", escapedEmail);
    }

    private static LdapConnection CreateConnection(LdapEndpoint endpoint)
    {
        var options = new LdapConnectionOptions();
        if (endpoint.Port == 636) options.UseSsl();

        // Allow self-signed certs in dev — caller accepts risk. In production,
        // configure proper CA trust and remove this callback.
        options.ConfigureRemoteCertificateValidationCallback(
            (sender, certificate, chain, sslPolicyErrors) => true);

        var conn = new LdapConnection(options)
        {
            ConnectionTimeout = endpoint.TimeoutSeconds * 1000
        };
        return conn;
    }
}
