using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Syntera.Backend.Data;
using Syntera.Backend.Middleware;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;
using Syntera.Backend.Tests.TestInfrastructure;

namespace Syntera.Backend.Tests;

/// <summary>
/// COMPLIANCE (Sprint 2.7) regression tests for the idle-timeout fix — the
/// surviving root cause of the reported "saat di refresh dia logout lagi"
/// (page reload logs the user out) after all transport/persistence/race fixes.
///
/// <para><b>The bug these tests guard against:</b>
/// <see cref="IRefreshTokenService.EnforceSessionLimitsAsync"/> measures idle
/// time as <c>UtcNow - RefreshToken.LastUsedAt</c>, but LastUsedAt was only
/// written at login and rotation. A user ACTIVELY using the app (valid Bearer
/// access token on every request → no 401 → no rotation) accumulated "idle"
/// time anyway. Once the session exceeded <c>Session:IdleMinutes</c> (30), the
/// FIRST refresh — an F5 page reload or the first 401 after the access token
/// expired — was killed with SESSION_IDLE_TIMEOUT and the cookie cleared.
/// In Development this fired deterministically: access tokens last 60 min
/// while the idle window is 30 min, so every session &gt; 30 min died.</para>
///
/// <para><b>The fix:</b> <see cref="SessionActivityMiddleware"/> stamps
/// LastUsedAt from authenticated API activity (throttled), so "idle" again
/// means "the user stopped making requests". Genuinely idle users still time
/// out (§11.300(d)); active users survive page reloads.</para>
///
/// <para>Harness note: each simulated "request" uses FRESH DbContexts over the
/// fixture's shared SQLite connections, mirroring one-scope-per-request DI.
/// Backdating and stamping go through fresh contexts / set-based updates so
/// EF identity resolution can never mask the persisted value.</para>
/// </summary>
public sealed class SessionActivityTests : IDisposable
{
    private readonly AuthFixture _f = new();

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>The F5 request: a brand-new scope running only the refresh flow
    /// (exactly what POST /api/auth/refresh resolves per request).</summary>
    private RefreshFlowService NewRefreshFlow()
    {
        var db = _f.NewPlatformContext();
        var current = new FakeCurrentUserService();
        var audit = new AuditService(db, _f.SiteFactory, current, NullLogger<AuditService>.Instance);
        IRefreshTokenService refreshTokens = new RefreshTokenService(_f.Tokens, _f.Config, audit);
        return new RefreshFlowService(
            db, _f.SiteFactory, audit, _f.Themes, _f.Permissions,
            NullLogger<RefreshFlowService>.Instance, current, refreshTokens);
    }

    /// <summary>The activity stamp as the middleware would issue it: fresh
    /// scope + dedicated throttle cache (a real request's IMemoryCache is
    /// scoped per request, so tests get a fresh one per "request").</summary>
    private SessionActivityService NewActivityService()
        => new(_f.NewPlatformContext(), _f.SiteFactory, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SessionActivityService>.Instance, _f.Config);

    /// <summary>Simulate time passing WITHOUT rotation (the active user's
    /// Bearer token keeps working, so LastUsedAt is never rewritten by the
    /// refresh flow itself).</summary>
    private async Task BackdatePlatformRowAsync(TimeSpan age)
    {
        using var db = _f.NewPlatformContext();
        var row = await db.RefreshTokens.SingleAsync();
        row.LastUsedAt = DateTime.UtcNow - age;
        await db.SaveChangesAsync();
    }

    private static async Task<DomainException> ThrowsAuthAsync(Func<Task> act)
        => await Assert.ThrowsAnyAsync<DomainException>(act);

    // ── THE reported bug: active user + F5 after the idle window ───────────

    [Fact]
    public async Task ApiActivity_KeepsSessionAlive_AcrossIdleWindow()
    {
        // REPRO of the user's report (pre-fix this FAILS with
        // SESSION_IDLE_TIMEOUT): login, work for 40 minutes (API calls with a
        // still-valid access token — no rotation happens), then F5.
        _f.SeedPlatformAdmin();
        var login = await _f.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");
        var adminId = login.Profile.UserId;

        // 40 minutes of ACTIVE usage — no rotation, but the middleware stamped
        // LastUsedAt on the API traffic throughout the session.
        await BackdatePlatformRowAsync(TimeSpan.FromMinutes(40)); // "now" is 40 min after login
        await NewActivityService().TouchAsync(adminId, "platform", siteId: null);

        // F5 — the reload must NOT idle-timeout: the user was demonstrably
        // active. The refresh succeeds and rotates the cookie.
        var refreshed = await NewRefreshFlow().RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent");
        Assert.NotEmpty(refreshed.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task NoApiActivity_StillTimesOut_AfterIdleWindow()
    {
        // Compliance guard (21 CFR Part 11 §11.300(d)): a GENUINELY idle
        // session — no requests for longer than IdleMinutes — must still be
        // terminated on the next refresh attempt.
        _f.SeedPlatformAdmin();
        var login = await _f.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        await BackdatePlatformRowAsync(TimeSpan.FromMinutes(40)); // idle, no stamps

        var ex = await ThrowsAuthAsync(() => NewRefreshFlow().RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("SESSION_IDLE_TIMEOUT", ex.Code);

        // And the timed-out token is revoked server-side.
        using var db = _f.NewPlatformContext();
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Touch_UpdatesOnlyActiveRows_RevokedReplacedExpiredUntouched()
    {
        // The stamp must never resurrect dead tokens: revoked, replaced, and
        // expired rows keep their original LastUsedAt (audit clarity).
        var admin = _f.SeedPlatformAdmin();
        await _f.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var old = DateTime.UtcNow.AddHours(-3);
        var family = await _f.PlatformDb.RefreshTokens.AsNoTracking().Select(r => r.FamilyId).SingleAsync();
        _f.PlatformDb.RefreshTokens.AddRange(
            Row("hash-revoked", revoked: true),
            Row("hash-replaced", replaced: true),
            Row("hash-expired", expired: true));
        await _f.PlatformDb.SaveChangesAsync();

        await NewActivityService().TouchAsync(admin.Id, "platform", siteId: null);

        using var check = _f.NewPlatformContext();
        Assert.All(await check.RefreshTokens.AsNoTracking()
            .Where(r => r.TokenHash.StartsWith("hash-")).ToListAsync(),
            r => Assert.Equal(old, r.LastUsedAt));
        var active = await check.RefreshTokens.AsNoTracking().SingleAsync(r => !r.TokenHash.StartsWith("hash-"));
        Assert.True(active.LastUsedAt > DateTime.UtcNow.AddMinutes(-1));

        RefreshToken Row(string hash, bool revoked = false, bool replaced = false, bool expired = false)
            => new()
            {
                Token = string.Empty,
                TokenHash = hash,
                UserId = admin.Id,
                UserScope = "platform",
                SiteId = null,
                ExpiresAt = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(1),
                RevokedAt = revoked ? DateTime.UtcNow.AddMinutes(-5) : null,
                ReplacedById = replaced ? Guid.NewGuid() : null,
                FamilyId = family,
                LastUsedAt = old,
            };
    }

    [Fact]
    public async Task Throttle_SecondTouchWithinWindow_DoesNotRewrite()
    {
        // One stamp per ActivityTouchSeconds per user — a burst of requests
        // within the window must not produce a second DB write.
        _f.SeedPlatformAdmin();
        var login = await _f.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        // First request in the window stamps; then the row is backdated again
        // (simulating another 40 idle minutes) and a second request arrives
        // within the SAME throttle window → no write → the next refresh times
        // out, proving the stamp was skipped.
        await BackdatePlatformRowAsync(TimeSpan.FromMinutes(40));
        var svc = NewActivityService();
        await svc.TouchAsync(login.Profile.UserId, "platform", siteId: null);

        await BackdatePlatformRowAsync(TimeSpan.FromMinutes(40));
        await svc.TouchAsync(login.Profile.UserId, "platform", siteId: null); // throttled — no-op

        var ex = await ThrowsAuthAsync(() => NewRefreshFlow().RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("SESSION_IDLE_TIMEOUT", ex.Code);
    }

    [Fact]
    public async Task SiteScope_TouchStampsSiteDatabaseRow()
    {
        // Site users: the stamp must land in the SITE's database (that is
        // where their refresh tokens — and the idle check — live).
        var siteId = _f.SeedSite("kalbe", "kalbe.co.id");
        var user = _f.SeedSiteUser(siteId, "qa@kalbe.co.id");

        var login = await _f.Auth.LoginAsync(
            new LoginRequest("qa@kalbe.co.id", "AnyLdap!Pass42"), ip: "10.0.0.2", userAgent: "test-agent");
        Assert.Equal("site", login.Profile.Scope);

        // Idle for 40 min (no rotation), then authenticated site API traffic.
        using (var siteDb = _f.SiteFactory.CreateContext(siteId))
        {
            var row = await siteDb.RefreshTokens.SingleAsync();
            row.LastUsedAt = DateTime.UtcNow.AddMinutes(-40);
            await siteDb.SaveChangesAsync();
        }

        await NewActivityService().TouchAsync(user.Id, "site", siteId);

        using (var check = _f.SiteFactory.CreateContext(siteId))
        {
            var row = await check.RefreshTokens.AsNoTracking().SingleAsync();
            Assert.True(row.LastUsedAt > DateTime.UtcNow.AddMinutes(-1),
                $"Expected a fresh LastUsedAt, got {row.LastUsedAt:O}");
        }
    }

    // ── Middleware gating (who counts as activity) ─────────────────────────

    private sealed class StubActivityService : ISessionActivityService
    {
        public List<(Guid UserId, string Scope, Guid? SiteId)> Calls { get; } = new();

        public Task TouchAsync(Guid userId, string scope, Guid? siteId, CancellationToken ct = default)
        {
            Calls.Add((userId, scope, siteId));
            return Task.CompletedTask;
        }
    }

    private static async Task<(StubActivityService Stub, bool NextCalled)> InvokeMiddlewareAsync(
        string path, bool authenticated, FakeCurrentUserService? current = null)
    {
        var stub = new StubActivityService();
        current ??= new FakeCurrentUserService { UserId = Guid.NewGuid(), Scope = "platform" };

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(current);
        services.AddSingleton<ISessionActivityService>(stub);
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider };
        ctx.Request.Path = path;
        if (authenticated)
        {
            var uid = current.UserId ?? Guid.NewGuid();
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
                    new Claim("scope", current.Scope ?? "platform"),
                },
                authenticationType: "Bearer"));
        }

        var nextCalled = false;
        var middleware = new SessionActivityMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(ctx);
        return (stub, nextCalled);
    }

    [Fact]
    public async Task Middleware_Stamps_OnAuthenticatedBusinessRequest()
    {
        var userId = Guid.NewGuid();
        var (stub, nextCalled) = await InvokeMiddlewareAsync(
            "/api/site/users", authenticated: true,
            current: new FakeCurrentUserService { UserId = userId, Scope = "site", SiteId = Guid.NewGuid() });

        Assert.Single(stub.Calls);
        Assert.Equal(userId, stub.Calls[0].UserId);
        Assert.Equal("site", stub.Calls[0].Scope);
        Assert.True(nextCalled, "The business request must always proceed to the next middleware.");
    }

    [Fact]
    public async Task Middleware_Skips_AuthEndpoints()
    {
        // /api/auth/* manages token lifecycle itself — never counts as activity.
        var (stub, _) = await InvokeMiddlewareAsync("/api/auth/refresh", authenticated: true);
        Assert.Empty(stub.Calls);
    }

    [Fact]
    public async Task Middleware_Skips_AnonymousRequests()
    {
        var (stub, nextCalled) = await InvokeMiddlewareAsync("/api/site/users", authenticated: false);
        Assert.Empty(stub.Calls);
        Assert.True(nextCalled);
    }

    public void Dispose() => _f.Dispose();
}
