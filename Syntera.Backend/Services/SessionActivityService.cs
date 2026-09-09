using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Entities;

namespace Syntera.Backend.Services;

/// <summary>
/// COMPLIANCE FIX (Sprint 2.7 — "F5/logout setelah sesi aktif &gt; IdleMinutes"):
/// stamps <see cref="RefreshToken.LastUsedAt"/> from AUTHENTICATED API ACTIVITY,
/// not only from token rotation.
///
/// <para><b>Why this exists:</b> the idle-timeout check
/// (<see cref="IRefreshTokenService.EnforceSessionLimitsAsync"/>) measures
/// "idle" as <c>UtcNow - LastUsedAt</c>. Before this fix, LastUsedAt was only
/// written at login and at rotation — a user who is ACTIVELY using the app
/// (every request carries a still-valid Bearer access token) never triggered a
/// refresh, so LastUsedAt went stale. Once the session exceeded
/// <c>Session:IdleMinutes</c> (default 30), the FIRST refresh attempt — a page
/// reload (F5) or the first 401 after the access token expired — was rejected
/// with <c>SESSION_IDLE_TIMEOUT</c>, revoking the token and clearing the cookie.
/// In Development this was a mathematical certainty: access tokens last 60 min
/// (<c>Jwt:AccessTokenMinutes</c>) while the idle window is 30 min, so every
/// session older than 30 minutes died on its first refresh.</para>
///
/// <para><b>Correct semantics (21 CFR Part 11 §11.300(d)):</b> "idle" means the
/// user stopped interacting with the system — NOT "no token rotation happened".
/// An actively-clicking user must keep their session; a user who closed the tab
/// (no requests) must still time out after IdleMinutes. This service keeps that
/// distinction by stamping LastUsedAt on successful authenticated requests,
/// throttled to one write per user per <c>Session:ActivityTouchSeconds</c>
/// (default 60s) so the stamping cost is one indexed UPDATE per user-minute in
/// the worst case.</para>
///
/// <para><b>Failure mode:</b> best-effort. A failed stamp logs a warning and
/// never fails the business request — a session-activity heuristic must not
/// take production traffic down. The idle check itself remains the enforcing
/// gate.</para>
/// </summary>
public interface ISessionActivityService
{
    /// <summary>
    /// Record "the user is active right now" on every ACTIVE refresh-token row
    /// of the given user (all rows: a user may hold several live
    /// sessions/devices). Throttled — at most one DB write per
    /// <c>Session:ActivityTouchSeconds</c> per user+scope.
    /// </summary>
    Task TouchAsync(Guid userId, string scope, Guid? siteId, CancellationToken ct = default);
}

public sealed class SessionActivityService : ISessionActivityService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SessionActivityService> _log;
    private readonly int _touchSeconds;

    public SessionActivityService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        IMemoryCache cache,
        ILogger<SessionActivityService> log,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _cache = cache;
        _log = log;
        _touchSeconds = config.GetValue<int>("Session:ActivityTouchSeconds", 60);
        if (_touchSeconds < 1) _touchSeconds = 60;
    }

    public async Task TouchAsync(Guid userId, string scope, Guid? siteId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return;
        if (scope != "platform" && scope != "site") return; // unknown scope — nothing to stamp

        // Throttle: one stamping write per user+scope per window. A cache hit
        // means a recent request already stamped this user. Set BEFORE the
        // write so concurrent requests in the same window are all suppressed
        // (the write itself is idempotent — a double write is harmless, but
        // the throttle keeps DB traffic flat under load).
        var key = $"session-activity:{scope}:{userId}";
        if (_cache.TryGetValue(key, out _)) return;
        _cache.Set(key, true, TimeSpan.FromSeconds(_touchSeconds));

        try
        {
            var now = DateTime.UtcNow;
            var rows = scope == "site"
                ? await StampSiteAsync(userId, siteId, now, ct)
                : await StampPlatformAsync(userId, now, ct);
            _log.LogDebug("SessionActivity: stamped {Rows} active refresh-token row(s) for user {UserId} ({Scope})", rows, userId, scope);
        }
        catch (Exception ex)
        {
            // BEST-EFFORT by design: never fail the business request. The idle
            // check at the next refresh remains the enforcement point.
            _log.LogWarning(ex, "SessionActivity: failed to stamp LastUsedAt for user {UserId} ({Scope}). Best-effort — request continues.", userId, scope);
        }
    }

    /// <summary>Stamp the ACTIVE rows in the platform master DB. Returns rows updated.</summary>
    private async Task<int> StampPlatformAsync(Guid userId, DateTime now, CancellationToken ct)
        => await _platformDb.Set<RefreshToken>()
            .Where(t => t.UserId == userId
                && t.UserScope == "platform"
                && t.RevokedAt == null
                && t.ReplacedById == null
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(sp => sp.SetProperty(t => t.LastUsedAt, now), ct);

    /// <summary>Stamp the ACTIVE rows in the SITE's database. Returns rows updated.</summary>
    private async Task<int> StampSiteAsync(Guid userId, Guid? siteId, DateTime now, CancellationToken ct)
    {
        if (siteId is null)
        {
            // A site-scoped JWT always carries site_id; defensive guard only.
            _log.LogDebug("SessionActivity: site scope without siteId claim — skipping stamp for user {UserId}", userId);
            return 0;
        }

        // ResolveForSiteAsync: request-cached context for the target site.
        // Business requests never load RefreshTokens into this context, so the
        // set-based ExecuteUpdate below is the only tracker interaction.
        var siteDb = await _siteDbFactory.ResolveForSiteAsync(siteId.Value, ct);
        return await siteDb.Set<RefreshToken>()
            .Where(t => t.UserId == userId
                && t.UserScope == "site"
                && t.RevokedAt == null
                && t.ReplacedById == null
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(sp => sp.SetProperty(t => t.LastUsedAt, now), ct);
    }
}
