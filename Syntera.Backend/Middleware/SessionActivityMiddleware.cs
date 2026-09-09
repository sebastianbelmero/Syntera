using Microsoft.AspNetCore.Http;
using Syntera.Backend.Services;

namespace Syntera.Backend.Middleware;

/// <summary>
/// COMPLIANCE FIX (Sprint 2.7): stamps refresh-token <c>LastUsedAt</c> from
/// authenticated API activity, so the §11.300(d) idle-timeout measures real
/// user inactivity instead of "time since the last token rotation"
/// (see <see cref="ISessionActivityService"/> for the full rationale).
///
/// <para>Placement: AFTER UseAuthentication/UseAuthorization — every request
/// that reaches this point with an authenticated principal counts as
/// activity. Auth endpoints (<c>/api/auth/*</c>) are excluded: login and
/// refresh have their own LastUsedAt semantics (issue + rotation), and the
/// anonymous refresh call must never be treated as "activity" for a family
/// whose token it is rotating.</para>
///
/// <para>The stamp is throttled inside the service (1 write per
/// Session:ActivityTouchSeconds per user) and is best-effort — a stamping
/// failure never fails the business request.</para>
/// </summary>
public sealed class SessionActivityMiddleware
{
    private readonly RequestDelegate _next;

    public SessionActivityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only authenticated requests indicate a live user. Anonymous traffic
        // (login page, /health, static assets) stamps nothing.
        if (context.User.Identity?.IsAuthenticated == true
            // Auth endpoints manage token lifecycle themselves — skip.
            && !context.Request.Path.StartsWithSegments("/api/auth"))
        {
            try
            {
                // Middleware is singleton-compatible: resolve the SCOPED
                // services (SessionActivityService + CurrentUserService)
                // per-request from the request's own service provider.
                var current = context.RequestServices.GetRequiredService<ICurrentUserService>();
                var userId = current.UserId;
                var scope = current.Scope;

                if (userId is not null && (scope == "platform" || scope == "site"))
                {
                    var activity = context.RequestServices.GetRequiredService<ISessionActivityService>();
                    await activity.TouchAsync(userId.Value, scope, current.SiteId, context.RequestAborted);
                }
            }
            catch (Exception)
            {
                // Best-effort by design — see the service. Swallow entirely:
                // stamping is a session heuristic, not a business operation.
            }
        }

        await _next(context);
    }
}
