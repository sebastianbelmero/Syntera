using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;
using Syntera.Backend.Tests.TestInfrastructure;

namespace Syntera.Backend.Tests;

/// <summary>
/// END-TO-END "F5 / page reload" simulation over the REAL service stack
/// (AuthFixture: SQLite per-database, real JWT/HMAC/rotation logic).
///
/// These tests reproduce EXACTLY what the browser does across a page reload:
/// <list type="number">
///   <item>REQUEST 1 (login): password (or MFA) → Set-Cookie syntera_refresh.</item>
///   <item>REQUEST 2 (the F5): a BRAND-NEW request scope — fresh DbContexts,
///     fresh controller, empty in-memory state — carrying ONLY the httpOnly
///     cookie. This is main.tsx → initAuth() → POST /api/auth/refresh.</item>
///   <item>The refresh must succeed, rotate the cookie, and return a fresh
///     access token + profile so the SPA re-renders authenticated routes.</item>
/// </list>
///
/// REGRESSION GUARD: the "login → F5 → login page" bug the users reported had
/// TWO server-side root causes, both of which make these tests fail:
/// <list type="bullet">
///   <item>P0-A: platform login/MFA never persisted the RefreshToken row —
///     the cookie pointed at a token the DB had never seen → REFRESH_NOT_FOUND
///     on every reload (fixed in the 2026-09 auth refactor).</item>
///   <item>P0-B: the multi-site fallback scan reused a cached context, so
///     site users' tokens were only found if they belonged to the FIRST site
///     (fixed via ISiteDbContextFactory.CreateForSiteAsync).</item>
/// </list>
///
/// The replay test documents the remaining HAZARD the frontend refresh
/// coordinator (single-flight + Web Locks, src/api/refreshCoordinator.ts)
/// exists to prevent: two refreshes presenting the SAME cookie → rotation
/// race → reuse detection → the whole token family (both tabs) is revoked.
/// </summary>
public sealed class AuthPageReloadFlowTests : IDisposable
{
    private readonly AuthFixture _f = new();

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>One simulated HTTP request: a fresh DI scope (fresh
    /// DbContexts over the SHARED SQLite databases) + a controller bound to
    /// a DefaultHttpContext that optionally carries the refresh cookie.</summary>
    private (AuthController Controller, DefaultHttpContext Ctx) NewRequest(string? refreshCookie = null)
    {
        var platformDb = _f.NewPlatformContext();
        var current = new FakeCurrentUserService();
        var audit = new AuditService(platformDb, _f.SiteFactory, current, NullLogger<AuditService>.Instance);
        IRefreshTokenService refreshTokens = new RefreshTokenService(_f.Tokens, _f.Config, audit);
        var platformAuth = new PlatformAdminAuthService(
            platformDb, _f.Tokens, _f.Hasher, audit, _f.Permissions, _f.Cache,
            NullLogger<PlatformAdminAuthService>.Instance, current, _f.Config, _f.PasswordPolicy, _f.Totp,
            refreshTokens);
        var siteAuth = new SiteUserAuthService(
            platformDb, _f.SiteFactory, _f.Ldap, audit, _f.Themes, _f.Permissions, _f.Cache,
            NullLogger<SiteUserAuthService>.Instance, refreshTokens);
        var refreshFlow = new RefreshFlowService(
            platformDb, _f.SiteFactory, audit, _f.Themes, _f.Permissions,
            NullLogger<RefreshFlowService>.Instance, current, refreshTokens);
        var auth = new AuthService(platformAuth, siteAuth, refreshFlow, _f.Cache);

        // Strict cookie-only mode (Auth:CookieOnlyRefreshToken omitted →
        // defaults to true) + scope TTL keys — mirrors production appsettings.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:RefreshTokenDaysPlatform"] = "1",
            ["Jwt:RefreshTokenDaysSite"] = "7",
        }).Build();

        var ctx = new DefaultHttpContext();
        if (refreshCookie is not null)
            ctx.Request.Headers.Cookie = $"syntera_refresh={refreshCookie}";

        var controller = new AuthController(auth, NullLogger<AuthController>.Instance,
            new ProdHost(), config)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
        return (controller, ctx);
    }

    /// <summary>Cookie value the browser would have stored after this response
    /// (parses the raw Set-Cookie header — no comma-joining surprises).</summary>
    private static string? RefreshCookieValue(DefaultHttpContext ctx)
    {
        foreach (var header in ctx.Response.Headers["Set-Cookie"])
        {
            var first = header.Split(';')[0].Trim();
            if (first.StartsWith("syntera_refresh=", StringComparison.Ordinal))
                return first["syntera_refresh=".Length..];
        }
        return null;
    }

    private static T Unwrap<T>(ObjectResult result) where T : class
        => Assert.IsType<ApiResponse<T>>(result.Value!).Data!;

    private static string ErrCode(ObjectResult result)
        => Assert.IsType<ApiResponse<object>>(result.Value!).ErrorCode!;

    /// <summary>Production-like host — Secure cookie flag set, strict mode on.</summary>
    private sealed class ProdHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Syntera.Backend.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ── Platform admin: the exact reported bug (login → F5 → login page) ──

    [Fact]
    public async Task PlatformLogin_ThenPageReload_RefreshesFromCookie_KeepsSessionAlive()
    {
        _f.SeedPlatformAdmin();

        // REQUEST 1 — login. Strict mode: the token leaves via Set-Cookie only.
        var (login, loginCtx) = NewRequest();
        var ok = Assert.IsType<OkObjectResult>(await login.Login(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            CancellationToken.None));
        var body = Unwrap<LoginResponse>(ok);
        Assert.False(body.RequiresMfa);
        Assert.False(body.RequiresPasswordChange);
        Assert.Equal(string.Empty, body.RefreshToken); // body transport closed
        var t1 = RefreshCookieValue(loginCtx);
        Assert.NotNull(t1);

        // P0-A regression guard: the login MUST have persisted the token row.
        using (var check = _f.NewPlatformContext())
            Assert.Equal(1, await check.RefreshTokens.AsNoTracking().CountAsync());

        // REQUEST 2 — the F5. Brand-new scope, empty body, cookie only.
        var (f5, f5Ctx) = NewRequest(t1);
        var ok2 = Assert.IsType<OkObjectResult>(await f5.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
        var body2 = Unwrap<RefreshResponse>(ok2);
        Assert.NotEmpty(body2.AccessToken);
        Assert.Equal("platform", body2.Profile.Scope);
        Assert.Equal(string.Empty, body2.RefreshToken);
        var t2 = RefreshCookieValue(f5Ctx);
        Assert.NotNull(t2);
        Assert.NotEqual(t1, t2); // rotated

        // REQUEST 3 — a SECOND consecutive reload with the rotated cookie.
        var (f5b, _) = NewRequest(t2);
        var ok3 = Assert.IsType<OkObjectResult>(await f5b.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
        Assert.NotEmpty(Unwrap<RefreshResponse>(ok3).AccessToken);
    }

    [Fact]
    public async Task PageReload_WithNoCookie_IsRejected_BodyTransportClosed()
    {
        _f.SeedPlatformAdmin();

        // No cookie in the jar (expired / first visit / another browser):
        // strict mode ignores the body token → EMPTY_TOKEN.
        var (f5, _) = NewRequest();
        var bad = Assert.IsType<BadRequestObjectResult>(await f5.Refresh(
            new RefreshRequest(RefreshToken: "body-token-fallback.random.sig"),
            CancellationToken.None));
        Assert.Equal("EMPTY_TOKEN", ErrCode(bad));
    }

    // ── Reuse detection: the multi-tab rotation-race hazard ────────────────

    [Fact]
    public async Task ReplayOfRotatedCookie_TriggersReuseDetection_AndKillsWholeFamily()
    {
        _f.SeedPlatformAdmin();

        var (login, loginCtx) = NewRequest();
        await login.Login(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            CancellationToken.None);
        var t1 = RefreshCookieValue(loginCtx)!;

        // First refresh rotates t1 → t2 (the normal F5).
        var (f5, f5Ctx) = NewRequest(t1);
        await f5.Refresh(new RefreshRequest(RefreshToken: null!), CancellationToken.None);
        var t2 = RefreshCookieValue(f5Ctx)!;

        // REPLAY of the already-rotated t1 — what happens when two tabs (or a
        // token thief) present the SAME cookie: family revoked, 401.
        var (replay, _) = NewRequest(t1);
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(await replay.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
        Assert.Equal("REFRESH_REUSE_DETECTED", ErrCode(unauthorized));

        // The entire family is now dead — t2 (the LEGITIMATE rotated token)
        // is revoked too. This is why the frontend must NEVER let two
        // refreshes race (single-flight + Web Locks in refreshCoordinator.ts):
        // two tabs F5-ing simultaneously log EACH OTHER out.
        var (f5c, _) = NewRequest(t2);
        Assert.IsType<UnauthorizedObjectResult>(await f5c.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
    }

    // ── Site users: F5 via the scan fallback (frontend has no profile yet) ─

    [Fact]
    public async Task SiteUserLogin_ThenPageReload_ScanFallback_KeepsSessionAlive()
    {
        var siteId = _f.SeedSite("kalbe", "kalbe.co.id");
        _f.SeedSiteUser(siteId, "qa@kalbe.co.id");

        // Login via LDAP (fake accepts) → site-scoped cookie.
        var (login, loginCtx) = NewRequest();
        var ok = Assert.IsType<OkObjectResult>(await login.Login(
            new LoginRequest("qa@kalbe.co.id", "AnyLdap!Pass42"), CancellationToken.None));
        var body = Unwrap<LoginResponse>(ok);
        Assert.Equal("site", body.Profile.Scope);
        Assert.Equal(siteId, body.Profile.SiteId);
        var t1 = RefreshCookieValue(loginCtx);
        Assert.NotNull(t1);

        // F5: the frontend has NO in-memory profile after a reload, so it calls
        // /auth/refresh — the backend must find the site token via the multi-site
        // scan (P0-B regression guard: the old cached-context scan missed it).
        var (f5, f5Ctx) = NewRequest(t1);
        var ok2 = Assert.IsType<OkObjectResult>(await f5.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
        var body2 = Unwrap<RefreshResponse>(ok2);
        Assert.Equal("site", body2.Profile.Scope);
        Assert.Equal(siteId, body2.Profile.SiteId);
        Assert.NotEmpty(body2.AccessToken);
        Assert.NotNull(RefreshCookieValue(f5Ctx));
    }

    [Fact]
    public async Task SiteUserLogin_ThenPageReload_RefreshSiteEndpoint_KeepsSessionAlive()
    {
        var siteId = _f.SeedSite("kalbe", "kalbe.co.id");
        _f.SeedSiteUser(siteId, "qa@kalbe.co.id");

        var (login, loginCtx) = NewRequest();
        await login.Login(new LoginRequest("qa@kalbe.co.id", "AnyLdap!Pass42"), CancellationToken.None);
        var t1 = RefreshCookieValue(loginCtx)!;

        // The 401-interceptor path: profile IS in memory (scope=site + siteId)
        // → POST /auth/refresh-site { siteId }, token via cookie.
        var (f5, f5Ctx) = NewRequest(t1);
        var ok2 = Assert.IsType<OkObjectResult>(await f5.RefreshSite(
            new RefreshSiteRequest(RefreshToken: null, SiteId: siteId), CancellationToken.None));
        var body2 = Unwrap<RefreshResponse>(ok2);
        Assert.Equal("site", body2.Profile.Scope);
        Assert.NotEmpty(body2.AccessToken);
        Assert.NotNull(RefreshCookieValue(f5Ctx));
    }

    // ── MFA flow: challenge sets no cookie; completion does; F5 survives ──

    [Fact]
    public async Task MfaLogin_ThenPageReload_RefreshesFromCookie_KeepsSessionAlive()
    {
        _f.SeedPlatformAdmin(totpEnabled: true, totpSecret: "enc:JBSWY3DPEHPK3PXP");

        // Step 1 — password OK, MFA required: challenge token, NO cookie.
        var (login, loginCtx) = NewRequest();
        var ok = Assert.IsType<OkObjectResult>(await login.Login(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            CancellationToken.None));
        var body = Unwrap<LoginResponse>(ok);
        Assert.True(body.RequiresMfa);
        Assert.NotNull(body.MfaChallengeToken);
        Assert.Null(RefreshCookieValue(loginCtx)); // challenge must not touch the cookie

        // Step 2 — MFA completion: full tokens + cookie.
        var (mfa, mfaCtx) = NewRequest();
        var ok2 = Assert.IsType<OkObjectResult>(await mfa.LoginMfa(
            new LoginMfaRequest(body.MfaChallengeToken!, _f.Totp.ValidCode), CancellationToken.None));
        var body2 = Unwrap<LoginResponse>(ok2);
        Assert.False(body2.RequiresMfa);
        Assert.NotEmpty(body2.AccessToken);
        var t1 = RefreshCookieValue(mfaCtx);
        Assert.NotNull(t1);

        // F5 — session survives the MFA login too.
        var (f5, _) = NewRequest(t1);
        var ok3 = Assert.IsType<OkObjectResult>(await f5.Refresh(
            new RefreshRequest(RefreshToken: null!), CancellationToken.None));
        Assert.NotEmpty(Unwrap<RefreshResponse>(ok3).AccessToken);
    }

    public void Dispose() => _f.Dispose();
}
