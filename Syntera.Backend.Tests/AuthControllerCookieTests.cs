using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;

namespace Syntera.Backend.Tests;

/// <summary>
/// HTTP-boundary tests for the H7 cookie-only refresh-token transport
/// (Auth:CookieOnlyRefreshToken). These complement the service-level
/// AuthRefreshRotationTests: the rotation semantics are proven there; HERE
/// we prove the transport contract:
///
/// <list type="bullet">
///   <item>Strict mode (default): the token must ARRIVE via the httpOnly
///     cookie — a body token is ignored (EMPTY_TOKEN when no cookie).</item>
///   <item>Strict mode: the token must LEAVE via Set-Cookie only — the JSON
///     body's RefreshToken field is blanked, so an XSS reading fetch/XHR
///     response bodies cannot exfiltrate it.</item>
///   <item>Non-strict mode (dev): body transport both ways still works for
///     Swagger/.http and integration harnesses (backward compat).</item>
///   <item>Challenge responses (MFA) never set a cookie.</item>
///   <item>Refresh failure clears the cookie (browser state matches the
///     revoked server state).</item>
///   <item>Cookie TTL mirrors the scope-specific refresh TTL (1d platform /
///     7d site) so the cookie never outlives the token.</item>
/// </list>
/// </summary>
public sealed class AuthControllerCookieTests
{
    private const string CookieName = "syntera_refresh";
    private const string IncomingToken = "incoming-cookie-token.randompart.sigpart";
    private const string RotatedToken = "rotated-token.newrandom.newsig";
    private const string BodyToken = "stale-body-token.oldrandom.oldsig";

    private readonly FakeAuthService _fake = new();

    // ── Harness ────────────────────────────────────────────────────────────

    private AuthController CreateController(bool cookieOnly, bool withCookie = true)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:RefreshTokenDaysPlatform"] = "1",
            ["Jwt:RefreshTokenDaysSite"] = "7",
        };
        if (!cookieOnly) dict["Auth:CookieOnlyRefreshToken"] = "false";
        // NOTE: when cookieOnly=true the key is deliberately OMITTED — the
        // controller must default to strict (secure-by-default config).
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ctx = new DefaultHttpContext();
        if (withCookie)
            ctx.Request.Headers.Cookie = $"{CookieName}={IncomingToken}";

        return new AuthController(_fake, NullLogger<AuthController>.Instance,
            new ProdHostEnvironment(), config)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
    }

    private static string SetCookies(AuthController controller)
        => controller.HttpContext!.Response.Headers["Set-Cookie"].ToString();

    /// <summary>
    /// AuthController inherits ApiControllerBase, whose Ok&lt;T&gt;() wraps the
    /// payload in the uniform ApiResponse envelope (same shape the frontend
    // unwraps in client.ts) — unwrap it here.
    /// </summary>
    private static T Unwrap<T>(ObjectResult result) where T : class
        => Assert.IsType<ApiResponse<T>>(result.Value!).Data!;

    private static UserProfileDto PlatformProfile() => new(
        Guid.NewGuid(), "admin@syntera.com", "Admin", null,
        "platform", null, null, null,
        new[] { "platform-admin" }, Array.Empty<string>());

    private static UserProfileDto SiteProfile() => new(
        Guid.NewGuid(), "qa@kalbe.co.id", "QA", "QA Officer",
        "site", Guid.NewGuid(), "kalbe", "PT Kalbe",
        new[] { "site-user" }, Array.Empty<string>());

    // ── Strict mode: refresh (platform scope) ──────────────────────────────

    [Fact]
    public async Task Refresh_StrictMode_UsesCookieToken_IgnoresBody()
    {
        var controller = CreateController(cookieOnly: true);

        var action = await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None);

        // The service must receive the COOKIE token — the body token is a
        // closed transport path in strict mode.
        Assert.Equal(IncomingToken, _fake.LastRefreshToken);
        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Refresh_StrictMode_BlanksBodyRefreshToken()
    {
        var controller = CreateController(cookieOnly: true);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None));

        var body = Unwrap<RefreshResponse>(ok);
        // XSS with response-body read access must see an EMPTY token.
        Assert.Equal(string.Empty, body.RefreshToken);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
    }

    [Fact]
    public async Task Refresh_StrictMode_SetsHttpOnlyPathScopedSecureCookie()
    {
        var controller = CreateController(cookieOnly: true);

        await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None);

        var cookies = SetCookies(controller);
        Assert.Contains(CookieName, cookies);
        // The ROTATED token (not the incoming one) must go into the cookie.
        Assert.Contains(RotatedToken, cookies);
        Assert.DoesNotContain(IncomingToken, cookies);
        Assert.True(cookies.Contains("httponly", StringComparison.OrdinalIgnoreCase), cookies);
        Assert.True(cookies.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase), cookies);
        Assert.True(cookies.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase), cookies);
        // Production (non-dev) host environment → Secure flag required.
        Assert.True(cookies.Contains("secure", StringComparison.OrdinalIgnoreCase), cookies);
    }

    [Fact]
    public async Task Refresh_PlatformScope_CookieTtlMatchesOneDay()
    {
        var controller = CreateController(cookieOnly: true);

        await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None);

        // Jwt:RefreshTokenDaysPlatform=1 → cookie must not outlive the token.
        var cookies = SetCookies(controller);
        Assert.True(cookies.Contains("max-age=86400", StringComparison.OrdinalIgnoreCase), cookies);
    }

    [Fact]
    public async Task Refresh_StrictMode_NoCookie_BodyOnly_Rejected()
    {
        var controller = CreateController(cookieOnly: true, withCookie: false);

        var action = await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None);

        // Body transport is closed in strict mode → treated as EMPTY_TOKEN.
        var bad = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal(400, bad.StatusCode);
        // Service must never be invoked with the body token.
        Assert.Null(_fake.LastRefreshToken);
    }

    [Fact]
    public async Task Refresh_Failure_ClearsCookie()
    {
        var controller = CreateController(cookieOnly: true);
        _fake.ThrowOnRefresh = new AuthenticationException("REFRESH_EXPIRED", "expired");

        var action = await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(action);
        // Browser state must match the (now-revoked) server state.
        var cookies = SetCookies(controller);
        Assert.Contains(CookieName, cookies);
        Assert.True(cookies.Contains("expires=", StringComparison.OrdinalIgnoreCase), cookies);
    }

    // ── Strict mode: site scope ────────────────────────────────────────────

    [Fact]
    public async Task RefreshSite_StrictMode_CookieTtlMatchesSevenDays()
    {
        var controller = CreateController(cookieOnly: true);
        var siteId = Guid.NewGuid();

        var ok = Assert.IsType<OkObjectResult>(
            await controller.RefreshSite(new RefreshSiteRequest(BodyToken, siteId), CancellationToken.None));

        // Site scope: 7-day TTL, token blanked from body, rotated token in
        // the cookie, and the COOKIE token (not the body one) forwarded.
        var cookies = SetCookies(controller);
        Assert.True(cookies.Contains("max-age=604800", StringComparison.OrdinalIgnoreCase), cookies);
        var body = Unwrap<RefreshResponse>(ok);
        Assert.Equal(string.Empty, body.RefreshToken);
        Assert.Equal(siteId, _fake.LastSiteId);
        Assert.Equal(IncomingToken, _fake.LastRefreshToken);
    }

    // ── Strict mode: login + challenges ────────────────────────────────────

    [Fact]
    public async Task Login_StrictMode_SetsCookie_ButBlanksBodyToken()
    {
        var controller = CreateController(cookieOnly: true);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.Login(new LoginRequest("admin@syntera.com", "pw"), CancellationToken.None));

        var cookies = SetCookies(controller);
        Assert.Contains(RotatedToken, cookies);
        Assert.True(cookies.Contains("httponly", StringComparison.OrdinalIgnoreCase), cookies);
        var body = Unwrap<LoginResponse>(ok);
        Assert.Equal(string.Empty, body.RefreshToken);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
    }

    [Fact]
    public async Task Login_MfaChallenge_DoesNotSetCookie()
    {
        var controller = CreateController(cookieOnly: true);
        _fake.LoginResult = new LoginResponse(
            AccessToken: "", ExpiresAt: DateTime.UtcNow, RefreshToken: "",
            Profile: PlatformProfile(), Theme: ThemeService.PlatformDefault(),
            RequiresMfa: true, MfaChallengeToken: "challenge-jwt");

        var ok = Assert.IsType<OkObjectResult>(
            await controller.Login(new LoginRequest("admin@syntera.com", "pw"), CancellationToken.None));

        // Challenge responses must NOT overwrite an existing session cookie.
        Assert.Equal(string.Empty, SetCookies(controller));
        var body = Unwrap<LoginResponse>(ok);
        Assert.True(body.RequiresMfa);
        Assert.Equal(string.Empty, body.RefreshToken);
    }

    [Fact]
    public async Task LoginMfa_StrictMode_SetsCookie_BlanksBody()
    {
        var controller = CreateController(cookieOnly: true);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.LoginMfa(new LoginMfaRequest("challenge-jwt", "123456"), CancellationToken.None));

        var cookies = SetCookies(controller);
        Assert.Contains(RotatedToken, cookies);
        var body = Unwrap<LoginResponse>(ok);
        Assert.Equal(string.Empty, body.RefreshToken);
    }

    // ── Logout ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_StrictMode_RevokesCookieToken_ClearsCookie()
    {
        var controller = CreateController(cookieOnly: true);

        var action = await controller.Logout(new LogoutRequest(BodyToken), CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        // The service must revoke the COOKIE token, not the ignored body one.
        Assert.Equal(IncomingToken, _fake.LastLogoutToken);
        Assert.Contains(CookieName, SetCookies(controller));
    }

    // ── Null-body transport (F5-LOGOUT FIX 2026-09-09) ─────────────────────
    // The cookie-only frontend posts bodies WITHOUT a refreshToken field
    // ("{}" / "{ siteId }"). The DTO nullability fix lets those requests
    // reach the action; these tests pin the controller behavior for a NULL
    // body token: the cookie is the credential, the body token is ignored.

    [Fact]
    public async Task Refresh_NullBodyToken_UsesCookieToken()
    {
        var controller = CreateController(cookieOnly: true);

        var action = await controller.Refresh(new RefreshRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(IncomingToken, _fake.LastRefreshToken);
    }

    [Fact]
    public async Task Logout_NullBodyToken_RevokesCookieToken()
    {
        var controller = CreateController(cookieOnly: true);

        var action = await controller.Logout(new LogoutRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(IncomingToken, _fake.LastLogoutToken);
    }

    // ── Non-strict (dev) mode: body transport preserved ────────────────────

    [Fact]
    public async Task Refresh_NonStrictMode_BodyFallbackStillWorks()
    {
        var controller = CreateController(cookieOnly: false, withCookie: false);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.Refresh(new RefreshRequest(BodyToken), CancellationToken.None));

        // Backward compat: the body token reaches the service AND the rotated
        // token is returned in the body (Swagger/.http debug tooling).
        Assert.Equal(BodyToken, _fake.LastRefreshToken);
        var body = Unwrap<RefreshResponse>(ok);
        Assert.Equal(RotatedToken, body.RefreshToken);
        // Cookie is still set too — both transports valid in dev.
        Assert.Contains(RotatedToken, SetCookies(controller));
    }

    // ── Test double ────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic IAuthService double — returns canned token pairs and
    /// records the refreshToken argument each method received, so tests can
    /// assert WHICH transport (cookie vs body) the controller forwarded.
    /// </summary>
    private sealed class FakeAuthService : IAuthService
    {
        internal string? LastRefreshToken;
        internal Guid? LastSiteId;
        internal string? LastLogoutToken;
        internal AuthenticationException? ThrowOnRefresh;
        internal LoginResponse LoginResult = DefaultLogin();

        public Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
            => Task.FromResult(LoginResult);

        public Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
        {
            if (ThrowOnRefresh is not null)
            {
                var ex = ThrowOnRefresh;
                ThrowOnRefresh = null;
                throw ex;
            }
            LastRefreshToken = refreshToken;
            return Task.FromResult(DefaultPlatformRefresh());
        }

        public Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default)
        {
            LastRefreshToken = refreshToken;
            LastSiteId = siteId;
            return Task.FromResult(DefaultSiteRefresh());
        }

        public Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
        {
            LastLogoutToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task ChangePasswordAsync(string currentPassword, string newPassword, string? ip, string? userAgent, CancellationToken ct = default, string? passwordChangeChallengeToken = null)
            => Task.CompletedTask;

        public Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default)
            => Task.FromResult(DefaultLogin());

        public Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default)
            => Task.FromResult(new MfaSetupResponse("otpauth://totp/Test", "JBSWY3DPEHPK3PXP"));

        public Task ConfirmMfaAsync(string code, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisableMfaAsync(string code, CancellationToken ct = default) => Task.CompletedTask;

        private static LoginResponse DefaultLogin() => new(
            AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RefreshToken: RotatedToken, Profile: PlatformProfile(), Theme: ThemeService.PlatformDefault());

        private static RefreshResponse DefaultPlatformRefresh() => new(
            AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RefreshToken: RotatedToken, Profile: PlatformProfile(), Theme: ThemeService.PlatformDefault());

        private static RefreshResponse DefaultSiteRefresh() => new(
            AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RefreshToken: RotatedToken, Profile: SiteProfile(), Theme: ThemeService.PlatformDefault());
    }

    /// <summary>Production-like host — Secure cookie flag must be set.</summary>
    private sealed class ProdHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Syntera.Backend.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
