using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;

/// <summary>
/// Site-user LDAP authentication flow, extracted from the former AuthService
/// god-class. Resolves the site by email domain (SiteLdapDomain table),
/// authenticates via the site's LDAP (direct bind), enforces pre-provisioning
/// (the User row must already exist in the site DB), syncs DisplayName/Title
/// from AD, resolves effective permissions, and issues + persists the token
/// pair in the SITE's database.
/// </summary>
public interface ISiteUserAuthService
{
    Task<LoginResponse> LoginAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct);
}

public sealed class SiteUserAuthService : ISiteUserAuthService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly ILdapClient _ldap;
    private readonly IAuditService _audit;
    private readonly IThemeService _themes;
    private readonly IPermissionService _permissions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SiteUserAuthService> _log;
    private readonly IRefreshTokenService _refreshTokens;

    public SiteUserAuthService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        ILdapClient ldap,
        IAuditService audit,
        IThemeService themes,
        IPermissionService permissions,
        IMemoryCache cache,
        ILogger<SiteUserAuthService> log,
        IRefreshTokenService refreshTokens)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _ldap = ldap;
        _audit = audit;
        _themes = themes;
        _permissions = permissions;
        _cache = cache;
        _log = log;
        _refreshTokens = refreshTokens;
    }

    public async Task<LoginResponse> LoginAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct)
    {
        var domain = email[(email.IndexOf('@') + 1)..];

        // ── Resolve site by email domain ──────────────────────────────
        var domainRow = await _platformDb.LdapDomains
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Domain == domain && d.IsActive, ct);

        if (domainRow is null || domainRow.Site is null || !domainRow.Site.IsEnabled)
        {
            LoginThrottle.BumpFail(_cache, perIpKey, perEmailKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "Site", TargetId: null,
                Outcome: "failure", ErrorMessage: $"Domain '{domain}' not registered"), ct);
            throw new Models.AuthenticationException("DOMAIN_NOT_REGISTERED",
                $"The email domain '{domain}' is not registered in this platform.");
        }

        var site = domainRow.Site;

        // ── Resolve LDAP config ───────────────────────────────────────
        var ldapConfig = await _platformDb.LdapConfigs
            .FirstOrDefaultAsync(c => c.SiteId == site.Id, ct);
        if (ldapConfig is null)
        {
            throw new Models.AuthenticationException("LDAP_NOT_CONFIGURED",
                $"Site '{site.Code}' has no LDAP configuration. Contact Platform Admin.");
        }

        var endpoint = new LdapEndpoint(
            Host: ldapConfig.Host,
            Port: ldapConfig.Port,
            UseStartTls: ldapConfig.UseStartTls,
            BaseDn: ldapConfig.BaseDn,
            UpnDomain: ldapConfig.UpnDomain);

        // ── Authenticate via LDAP (direct bind: user's own email + password) ──
        var result = await _ldap.AuthenticateAsync(endpoint, email, password, ct);
        if (!result.IsSuccess)
        {
            LoginThrottle.BumpFail(_cache, perIpKey, perEmailKey);
            // SECURITY (H5): audit log keeps the detailed internal error for
            // forensic/admin debugging, but the user-facing exception is
            // genericized to prevent account enumeration / info leakage.
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: null,
                Outcome: "failure", ErrorMessage: result.ErrorMessage), ct);
            var publicMessage = MapLdapErrorToPublic(result.ErrorMessage);
            throw new Models.AuthenticationException("LDAP_AUTH_FAILED", publicMessage);
        }

        // ── Pre-provisioning check: user must exist in site DB ────────
        // Use ResolveForSiteAsync(site.Id) — at login time there is no JWT yet,
        // so ResolveAsync(ct) (which reads JWT site_id claim) would throw.
        var siteDb = await _siteDbFactory.ResolveForSiteAsync(site.Id, ct);
        var user = await siteDb.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: null,
                Outcome: "failure", ErrorMessage: "User not provisioned in site"), ct);
            throw new Models.AuthenticationException("USER_NOT_PROVISIONED",
                "Your account has not been provisioned. Contact your Site Business Admin.");
        }

        if (!user.IsEnabled)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: user.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: user.Id.ToString(),
                Outcome: "failure", ErrorMessage: "User disabled in site"), ct);
            throw new Models.AuthenticationException("USER_DISABLED",
                "Your account is disabled. Contact your Site Business Admin.");
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
        {
            throw new Models.AuthenticationException("ACCOUNT_LOCKED",
                $"Account locked until {user.LockedUntil.Value:u}.");
        }

        // ── Auto-sync DisplayName + Title from LDAP on every login ─────
        // LDAP returns null when AD doesn't have the attribute. We must NOT
        // overwrite the DB value with null/empty — that would erase the manual
        // value the Business Admin set during pre-provisioning. Only update
        // when LDAP gives us real data AND it differs.
        var changed = false;
        if (!string.IsNullOrEmpty(result.DisplayName) && result.DisplayName != user.DisplayName)
        {
            user.DisplayName = result.DisplayName;
            changed = true;
        }
        if (result.Title is not null && result.Title != user.Title)
        {
            user.Title = result.Title;
            changed = true;
        }
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        if (changed)
            _log.LogInformation("LDAP sync: user {Email} DisplayName/Title updated from AD", email);
        await siteDb.SaveChangesAsync(ct);

        // ── Resolve effective permissions ─────────────────────────────
        var (roles, perms) = await _permissions.ResolveForUserAsync(siteDb, user.Id, ct);
        var siteDisplay = site.DisplayName;

        var profile = new UserProfileDto(
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName, Title: user.Title,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: siteDisplay,
            Roles: roles, Permissions: perms);

        var (access, exp, refresh) = await _refreshTokens.IssueTokensAsync(
            userId: user.Id, scope: "site", siteId: site.Id,
            email: user.Email, displayName: user.DisplayName, title: user.Title,
            roles: roles, permissions: perms,
            version: user.PermissionsVersion, ip: ip, ua: ua, ct: ct);

        // Persist the refresh token in the SITE's database.
        var rt = _refreshTokens.BuildToken(user.Id, "site", site.Id, refresh, ip, ua);
        siteDb.RefreshTokens.Add(rt);
        await siteDb.SaveChangesAsync(ct);

        // M4: clear BOTH rate-limit buckets on success.
        _cache.Remove(perIpKey);
        _cache.Remove(perEmailKey);

        var theme = await _themes.GetThemeAsync(site.Id, ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: site.Id, ActorUserId: user.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
            Action: "auth.login", TargetType: "User", TargetId: user.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        return new LoginResponse(access, exp, refresh, profile, theme);
    }

    /// <summary>
    /// SECURITY (H5): map detailed LDAP server errors to a small set of
    /// generic user-facing messages. The detailed error is preserved in the
    /// audit log for forensic/admin debugging. Disabled account stays
    /// explicit (legitimate UX); server/transport errors collapse to
    /// "service unavailable"; everything else collapses to "Invalid email or
    /// password" so attackers cannot distinguish failure reasons.
    /// </summary>
    private static string MapLdapErrorToPublic(string? internalMessage)
    {
        if (string.IsNullOrEmpty(internalMessage))
            return "Invalid email or password.";

        // Disabled account: keep explicit for UX (B2B internal — admin
        // disabling a user wants the user to know why login fails).
        if (internalMessage.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            return "Your account is disabled. Contact your Site Business Admin.";

        // Server / transport failures: don't leak user existence.
        if (internalMessage.Contains("server error", StringComparison.OrdinalIgnoreCase)
            || internalMessage.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
            || internalMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return "Authentication service unavailable, please try again later.";

        // Everything else (invalid creds, multiple matches, bind code X): generic.
        return "Invalid email or password.";
    }
}
