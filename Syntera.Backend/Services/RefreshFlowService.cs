using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;

/// <summary>
/// Refresh + logout flows, extracted from the former AuthService god-class.
/// RefreshAsync handles platform tokens directly and falls back to scanning
/// every enabled site's DB for site-scope tokens (the frontend cannot know
/// the siteId after a page reload). RefreshSiteAsync is the site-scoped
/// entry point. Both use TOCTOU-safe atomic rotation (conditional UPDATE +
/// single INSERT) and family-wide revocation on reuse detection.
/// </summary>
public interface IRefreshFlowService
{
    Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);
    Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default);
}

public sealed class RefreshFlowService : IRefreshFlowService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly IAuditService _audit;
    private readonly IThemeService _themes;
    private readonly IPermissionService _permissions;
    private readonly ILogger<RefreshFlowService> _log;
    private readonly ICurrentUserService _currentUser;
    private readonly IRefreshTokenService _refreshTokens;

    public RefreshFlowService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        IAuditService audit,
        IThemeService themes,
        IPermissionService permissions,
        ILogger<RefreshFlowService> log,
        ICurrentUserService currentUser,
        IRefreshTokenService refreshTokens)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _audit = audit;
        _themes = themes;
        _permissions = permissions;
        _log = log;
        _currentUser = currentUser;
        _refreshTokens = refreshTokens;
    }

    public async Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new Models.AuthenticationException("INVALID_REFRESH", "Refresh token is required.");

        // L3: fast-reject forged tokens before DB lookup. REFRESH_NOT_FOUND is
        // returned (same as a real miss) so the failure mode doesn't leak that
        // the signature check failed. Length only in logs — never token material.
        _log.LogDebug("RefreshAsync: token length={Len}", refreshToken.Length);

        if (!_refreshTokens.VerifySignature(refreshToken))
        {
            _log.LogWarning("RefreshAsync: SIGNATURE VERIFICATION FAILED (len={Len}). Likely cause: stale token from before backend restart, or Jwt:SigningKey changed between requests.", refreshToken.Length);
            throw new Models.AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");
        }

        _log.LogDebug("RefreshAsync: signature OK. Now looking up in DB.");

        // L3: hash only the random part — signature suffix is server-derived
        // and adds no entropy.
        var hash = _refreshTokens.HashToken(refreshToken);

        // Look in platform DB first (platform admin).
        // M1: include tokens that are already revoked in the lookup so we can
        // detect reuse — if a token was already rotated (ReplacedById is set
        // OR RevokedAt is set) and someone presents it again, that's theft.
        var platformToken = await _platformDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        _log.LogDebug("RefreshAsync: platform DB lookup. Found={Found}", platformToken is not null);
        if (platformToken is not null)
        {
            // M1 — Token reuse detection: if this token was already rotated
            // or revoked, but someone is presenting it again, the legitimate
            // user already moved on (rotated to a new token). Revoke the
            // entire family — force re-authentication.
            if (platformToken.RevokedAt is not null || platformToken.ReplacedById is not null)
                return await HandleReuseDetectedAsync(platformToken, _platformDb, siteId: null, ip, userAgent, "platform scope", ct);

            if (platformToken.ExpiresAt <= DateTime.UtcNow)
                throw new Models.AuthenticationException("REFRESH_EXPIRED", "Refresh token expired.");

            // COMPLIANCE (Sprint 2.5): idle session timeout + max session age.
            await _refreshTokens.EnforceSessionLimitsAsync(platformToken, _platformDb, ip, userAgent, ct);

            var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformToken.UserId, ct);
            if (admin is null || !admin.IsEnabled)
                throw new Models.AuthenticationException("USER_NOT_FOUND", "Platform admin no longer exists.");

            var profile = new UserProfileDto(
                UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
                Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
                Roles: AuthConstants.PlatformAdminRoleClaim,
                Permissions: _permissions.GetPlatformAdminPermissions());

            return await RotateAndRespondAsync(platformToken, _platformDb, admin.Id, "platform", siteId: null,
                profile, ip, userAgent, platformToken.FamilyId, theme: null, version: 1, ct: ct);
        }

        // H7-full (Sprint 4): if the token wasn't in the platform DB, it
        // might be a site-scope token. On a fresh page load, the frontend
        // doesn't know which site the user belongs to (no in-memory
        // profile), so it can't call /api/auth/refresh-site with siteId.
        // We auto-detect by scanning all enabled sites' RefreshTokens.
        // Performance: 6 sites typical, indexed by TokenHash (unique) —
        // each lookup is O(1). Total worst case = 6 PK lookups.
        _log.LogDebug("RefreshAsync: token not in platform DB. Scanning {Count} site DBs.", await _platformDb.Sites.CountAsync(s => s.IsEnabled, ct));
        var allSites = await _platformDb.Sites.AsNoTracking()
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

        foreach (var site in allSites)
        {
            // FIX (P0 — multi-site scan bug): use CreateForSiteAsync (fresh,
            // UNCACHED context) — the previous code called ResolveForSiteAsync,
            // which caches the FIRST site's context per request and ignores the
            // siteId argument on subsequent calls. Iterations 2..N then queried
            // the FIRST site's database repeatedly, so tokens belonging to any
            // other site were never found → REFRESH_NOT_FOUND.
            SiteDbContext siteDb;
            try
            {
                siteDb = await _siteDbFactory.CreateForSiteAsync(site.Id, ct);
            }
            catch
            {
                // Site DB unreachable — skip, try next site. Don't let one
                // broken site DB block refresh for everyone.
                continue;
            }

            try
            {
                var siteToken = await siteDb.RefreshTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
                _log.LogDebug("RefreshAsync: site {Code} lookup. Found={Found}", site.Code, siteToken is not null);
                if (siteToken is null)
                    continue;

                // Found in this site's DB — delegate to the site-scoped refresh
                // logic (which handles reuse detection, family revocation, etc).
                // (RefreshSiteAsync resolves its own request-cached context for
                // the target site, so disposing our scan context here is safe.)
                _log.LogDebug("RefreshAsync: delegating to RefreshSiteAsync for site {Code}", site.Code);
                return await RefreshSiteAsync(refreshToken, site.Id, ip, userAgent, ct);
            }
            finally
            {
                await siteDb.DisposeAsync();
            }
        }

        // Not found in platform DB nor any site DB — genuine miss.
        _log.LogWarning("RefreshAsync: token NOT FOUND in any DB (platform + {Count} sites). This means the token is stale (from before backend restart), or the user's session was revoked.", allSites.Count);
        throw new Models.AuthenticationException("REFRESH_NOT_FOUND",
            "Refresh token not found.");
    }

    public async Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        // L3: signature check before DB lookup (same rationale as RefreshAsync).
        if (!_refreshTokens.VerifySignature(refreshToken))
            throw new Models.AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        var hash = _refreshTokens.HashToken(refreshToken);

        // Verify site exists.
        var site = await _platformDb.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new Models.NotFoundException("Site", siteId);

        var siteDb = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);
        // M1: include tokens that are already revoked in the lookup so we can
        // detect reuse — see RefreshAsync for full explanation.
        var token = await siteDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new Models.AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        // M1 — Token reuse detection (site scope): same logic as platform.
        if (token.RevokedAt is not null || token.ReplacedById is not null)
            return await HandleReuseDetectedAsync(token, siteDb, siteId: siteId, ip, userAgent, $"site {site.Code}", ct);

        if (token.ExpiresAt <= DateTime.UtcNow)
            throw new Models.AuthenticationException("REFRESH_EXPIRED", "Refresh token expired.");

        // COMPLIANCE (Sprint 2.5): idle session timeout + max session age (site scope).
        await _refreshTokens.EnforceSessionLimitsAsync(token, siteDb, ip, userAgent, ct);

        var user = await siteDb.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct)
            ?? throw new Models.AuthenticationException("USER_NOT_FOUND", "User no longer exists.");

        if (!user.IsEnabled)
            throw new Models.AuthenticationException("USER_DISABLED", "User is disabled.");

        var (roles, perms) = await _permissions.ResolveForUserAsync(siteDb, user.Id, ct);
        var profile = new UserProfileDto(
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName, Title: user.Title,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: site.DisplayName,
            Roles: roles, Permissions: perms);

        var theme = await _themes.GetThemeAsync(site.Id, ct);
        return await RotateAndRespondAsync(token, siteDb, user.Id, "site", siteId: site.Id,
            profile, ip, userAgent, token.FamilyId, theme: theme, version: user.PermissionsVersion, ct: ct);
    }

    public async Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
    {
        // L3: signature check before DB lookup. Even on logout we don't
        // want an attacker to be able to force DB queries with garbage tokens.
        // If signature fails, silently succeed (logout is idempotent — there's
        // nothing to revoke). Don't leak the failure mode.
        if (!_refreshTokens.VerifySignature(refreshToken))
            return;
        var hash = _refreshTokens.HashToken(refreshToken);
        var platformToken = await _platformDb.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (platformToken is not null)
        {
            await RevokeFamilyAndSaveAsync(_platformDb, platformToken, revokedBy, ct);
            return;
        }

        // Site-scope token. Fast path: the caller's own site from the (still
        // valid) access token's claims...
        var siteId = _currentUser.SiteId;
        if (siteId is not null)
        {
            var siteDb = await _siteDbFactory.ResolveForSiteAsync(siteId.Value, ct);
            // LOGOUT-RELOGIN FIX: no RevokedAt==null filter — a token that was
            // already rotated must still be traceable to its live successor
            // via FamilyId.
            var siteToken = await siteDb.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (siteToken is not null)
            {
                await RevokeFamilyAndSaveAsync(siteDb, siteToken, revokedBy, ct);
                return;
            }
        }

        // ...then the same multi-site scan RefreshAsync uses. This covers the
        // anonymous-logout case (expired/absent access token — no claims, so
        // no siteId; the httpOnly cookie is the only credential) and a claims
        // site that simply didn't contain the token.
        var allSites = await _platformDb.Sites.AsNoTracking()
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);
        foreach (var site in allSites)
        {
            if (site.Id == siteId) continue; // already tried above
            SiteDbContext scanDb;
            try
            {
                // Same pattern as RefreshAsync: fresh UNCACHED context per
                // site — ResolveForSiteAsync caches the FIRST site's context
                // per request and ignores the siteId on later calls.
                scanDb = await _siteDbFactory.CreateForSiteAsync(site.Id, ct);
            }
            catch
            {
                // Site DB unreachable — skip, try next site. Don't let one
                // broken site DB block logout.
                continue;
            }
            try
            {
                var siteToken = await scanDb.RefreshTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
                if (siteToken is null) continue;
                await RevokeFamilyAndSaveAsync(scanDb, siteToken, revokedBy, ct);
                return;
            }
            finally
            {
                await scanDb.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// LOGOUT-RELOGIN FIX (2026-09-09): logout revokes the token's entire
    /// FAMILY, not just the presented row. Refresh tokens rotate on every
    /// silent refresh (page reload, 401 interceptor); if a rotation is in
    /// flight while the user logs out — or its Set-Cookie lands after the
    /// logout response — the presented row is already consumed but its
    /// successor is live. Revoking only the presented row left the successor
    /// valid: the browser's late-arriving Set-Cookie re-authenticated the
    /// user on the next app boot ("logout → instant re-login"). Family
    /// revocation kills the successor too, and any later use of a family
    /// member still trips REFRESH_REUSE_DETECTED.
    /// </summary>
    private async Task RevokeFamilyAndSaveAsync(
        Microsoft.EntityFrameworkCore.DbContext db,
        RefreshToken token,
        Guid? revokedBy,
        CancellationToken ct)
    {
        await _refreshTokens.RevokeFamilyAsync(
            db.Set<RefreshToken>(), token.FamilyId, revokedBy ?? token.UserId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Shared rotation tail for both scopes. TOCTOU-safe:
    /// <list type="number">
    ///   <item>Issue the new token pair + build the new row (Id is pre-generated).</item>
    ///   <item>Conditional UPDATE on the OLD row (WHERE RevokedAt IS NULL AND
    ///     ReplacedById IS NULL) — exactly one concurrent request can win; the
    ///     loser falls into the reuse path.</item>
    ///   <item>Insert the new row + single SaveChanges.</item>
    /// </list>
    /// ReplacedById is set on the OLD token pointing to the NEW token's
    /// pre-generated Id — never on the new token (commit 37013ca semantics).
    /// </summary>
    private async Task<RefreshResponse> RotateAndRespondAsync(
        RefreshToken oldToken,
        Microsoft.EntityFrameworkCore.DbContext db,
        Guid userId,
        string scope,
        Guid? siteId,
        UserProfileDto profile,
        string? ip,
        string? userAgent,
        Guid? familyId,
        Models.Dtos.Auth.ThemeDto? theme,
        long version,
        CancellationToken ct)
    {
        var (access, exp, newRefresh) = await _refreshTokens.IssueTokensAsync(
            userId, scope, siteId, profile.Email, profile.DisplayName, profile.Title,
            profile.Roles, profile.Permissions, version, ip, userAgent, ct);

        // M1: propagate FamilyId from parent so all tokens in a chain share it.
        var newRt = _refreshTokens.BuildToken(userId, scope, siteId, newRefresh, ip, userAgent, familyId: familyId);
        newRt.LastUsedAt = DateTime.UtcNow;

        // FIX (P2 — atomic rotation): the previous flow used THREE separate
        // SaveChangesAsync calls (revoke old → insert new → set ReplacedById),
        // leaving windows where a crash or a concurrent refresh could produce
        // inconsistent state (e.g. two live tokens in one family when two tabs
        // refresh simultaneously). Now: one conditional UPDATE + one INSERT.
        var rotatedAt = DateTime.UtcNow;
        var rotatedRows = await db.Set<RefreshToken>()
            .Where(t => t.Id == oldToken.Id && t.RevokedAt == null && t.ReplacedById == null)
            .ExecuteUpdateAsync(sp => sp
                .SetProperty(t => t.RevokedAt, rotatedAt)
                .SetProperty(t => t.RevokedBy, (Guid?)oldToken.UserId)
                .SetProperty(t => t.ReplacedById, (Guid?)newRt.Id), ct);

        if (rotatedRows == 0)
            return await HandleReuseDetectedAsync(oldToken, db, siteId: siteId, ip, userAgent, $"{scope} scope (rotation race)", ct);

        db.Set<RefreshToken>().Add(newRt);
        await db.SaveChangesAsync(ct);

        return new RefreshResponse(access, exp, newRefresh, profile, theme ?? ThemeService.PlatformDefault());
    }

    /// <summary>
    /// SECURITY (M1) + FIX (P1): shared handler for token-reuse detection.
    /// Revokes the token's entire family, PERSISTS the revocation, writes the
    /// audit entry, and throws REFRESH_REUSE_DETECTED.
    ///
    /// <para><b>Why the explicit SaveChanges matters:</b> the previous code
    /// relied on the subsequent <c>_audit.LogAsync</c> call to flush the pending
    /// family revocations (both happen to share the same scoped DbContext).
    /// That worked only by accident — and failed OPEN when the audit write
    /// failed, silently losing the revocation while the audit entry still
    /// claimed "family revoked". The revocation is now persisted FIRST,
    /// independent of audit outcome.</para>
    /// </summary>
    private async Task<RefreshResponse> HandleReuseDetectedAsync(
        RefreshToken token, Microsoft.EntityFrameworkCore.DbContext db, Guid? siteId, string? ip, string? userAgent, string logScope, CancellationToken ct)
    {
        _log.LogWarning("Refresh token reuse detected ({Scope}, family {FamilyId}). Revoking family.", logScope, token.FamilyId);
        await _refreshTokens.RevokeFamilyAsync(db.Set<RefreshToken>(), token.FamilyId, revokedBy: token.UserId, ct);
        // FIX (P1): persist the family revocation NOW — do not depend on the
        // audit write to flush it.
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync(new AuditEntry(
            SiteId: siteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: userAgent,
            Action: "auth.refresh", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
            Outcome: "failure", ErrorMessage: "Token reuse detected — family revoked"), ct);
        throw new Models.AuthenticationException("REFRESH_REUSE_DETECTED",
            "Refresh token reuse detected. Please log in again.");
    }
}
