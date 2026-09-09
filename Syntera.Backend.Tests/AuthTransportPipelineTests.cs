using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syntera.Backend.Controllers;
using Syntera.Backend.Extensions;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;

namespace Syntera.Backend.Tests;

/// <summary>
/// FULL-PIPELINE transport tests (TestServer + the REAL AddSynteraMvc
/// registration — model binding, the [ApiController] auto-validation filter,
/// and the custom InvalidModelStateResponseFactory all run, exactly like in
/// Program.cs).
///
/// WHY THIS FILE EXISTS (F5-logout postmortem, 2026-09-09):
/// the existing AuthControllerCookieTests invoke the action methods
/// DIRECTLY — the MVC pipeline (model binding + implicit [Required]
/// validation from non-nullable reference parameters + the auto-400 filter)
/// never runs in those tests. That is how the "logout on every refresh"
/// bug stayed invisible through three rounds of fixes:
///
///   The cookie-only frontend posts POST /api/auth/refresh with body "{}"
///   (the refresh token travels in the httpOnly cookie, not the body).
///   The request DTO was `RefreshRequest(string RefreshToken)` —
///   non-nullable → implicitly [Required] → the auto-validation filter
///   returned 400 VALIDATION_FAILED ("The RefreshToken field is
///   required.") BEFORE the action executed. The cookie was never read,
///   every silent refresh (app boot after F5, the 401 interceptor) failed,
///   and the app logged the user out. Signature in the backend log:
///   400 responses with NO ReadRefreshToken debug line and NO
///   "Refresh failed: code=..." warning — the action never ran.
///
/// These tests bind through the real pipeline using the exact frontend
/// wire format ("{}" / "{ \"siteId\": ... }" + Cookie header), so a DTO
/// nullability regression can never hide behind direct action calls again.
/// </summary>
public sealed class AuthTransportPipelineTests
{
    private const string CookieName = "syntera_refresh";
    private const string IncomingToken = "incoming-cookie-token.randompart.sigpart";
    private const string RotatedToken = "rotated-token.newrandom.newsig";

    // ── Harness: real MVC pipeline over a TestServer ───────────────────────

    private sealed class PipelineHost : IDisposable
    {
        internal HttpClient Client { get; }
        internal PipelineFakeAuthService Fake { get; }
        private readonly WebApplication _app;

        internal PipelineHost(HttpClient client, PipelineFakeAuthService fake, WebApplication app)
        {
            Client = client;
            Fake = fake;
            _app = app;
        }

        public void Dispose()
        {
            Client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static PipelineHost CreateHost()
    {
        var fake = new PipelineFakeAuthService();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production", // Secure cookie flags like prod
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders(); // keep test output clean
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:CookieOnlyRefreshToken"] = "true", // strict mode (prod default)
            ["Jwt:RefreshTokenDaysPlatform"] = "1",
            ["Jwt:RefreshTokenDaysSite"] = "7",
            // LOGOUT-RELOGIN FIX (2026-09-09): required by AddSynteraSecurity
            // (≥ 32 chars) so the JWT bearer middleware is actually active in
            // the pipeline — [Authorize] endpoints really 401 without a
            // Bearer, exactly like in Program.cs. WebApplication auto-inserts
            // UseAuthentication/UseAuthorization when the services exist.
            ["Jwt:SigningKey"] = "pipeline-test-signing-key-0123456789abcdef",
        });

        // REAL MVC stack: SuppressModelStateInvalidFilter = false (the
        // auto-validation filter is ACTIVE) + ApiResponse envelope factory
        // + camelCase JSON — the same registration Program.cs uses.
        builder.Services.AddSynteraMvc();
        // REAL security stack (JWT bearer + DefaultPolicy
        // RequireAuthenticatedUser) — see Jwt:SigningKey above.
        builder.Services.AddSynteraSecurity(builder.Configuration);
        // The test assembly is the "entry" assembly and does not generate
        // ApplicationPart attributes for Syntera.Backend (non-Web SDK), so
        // the backend controllers must be added explicitly.
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddScoped<IAuthService>(_ => fake);

        var app = builder.Build();
        app.MapControllers();
        app.Start();

        var server = (TestServer)app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        return new PipelineHost(server.CreateClient(), fake, app);
    }

    private static HttpRequestMessage JsonPost(string url, string json)
        => new(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JsonDocument ReadJson(HttpResponseMessage resp)
        => JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());

    // ── POST /api/auth/refresh — the exact F5 / app-boot request ───────────

    [Fact]
    public async Task Refresh_EmptyBody_NoCookie_ReachesAction_ReturnsEmptyToken()
    {
        // Regression (pre-fix): 400 VALIDATION_FAILED from the auto-validation
        // filter — the action never ran and the cookie was never read.
        using var host = CreateHost();

        var resp = await host.Client.SendAsync(JsonPost("/api/auth/refresh", "{}"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = ReadJson(resp).RootElement;
        // The action's OWN EMPTY_TOKEN — proof the request reached the
        // controller action and only THEN failed (no cookie, no body token).
        Assert.Equal("EMPTY_TOKEN", body.GetProperty("errorCode").GetString());
        // Service never invoked.
        Assert.Null(host.Fake.LastRefreshToken);
    }

    [Fact]
    public async Task Refresh_EmptyBody_WithCookie_Refreshes_RotatesCookie_DoesNotLeakToken()
    {
        // THE F5-logout repro: app boot posts "{}" + the httpOnly cookie.
        // Regression (pre-fix): 400 VALIDATION_FAILED → silent refresh
        // failed on every page reload → user logged out.
        using var host = CreateHost();

        var req = JsonPost("/api/auth/refresh", "{}");
        req.Headers.Add("Cookie", $"{CookieName}={IncomingToken}");
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // The COOKIE token (not a body token — there is none) reached the service.
        Assert.Equal(IncomingToken, host.Fake.LastRefreshToken);
        // The rotated token went out via Set-Cookie (httpOnly, path-scoped).
        var setCookie = string.Join("; ", resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains(RotatedToken, setCookie);
        Assert.Contains(CookieName, setCookie);
        // Strict mode: the JSON body must NOT carry the rotated token.
        var body = ReadJson(resp).RootElement;
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(string.Empty, body.GetProperty("data").GetProperty("refreshToken").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    // ── POST /api/auth/refresh-site — site scope, { siteId } body ──────────

    [Fact]
    public async Task RefreshSite_SiteIdOnlyBody_WithCookie_ReachesAction()
    {
        // The 401-interceptor path for site users: body carries ONLY the
        // (non-secret) siteId; the httpOnly cookie is the credential.
        // Regression (pre-fix): implicit [Required] on RefreshToken killed
        // this request in model validation → 400 → forced logout.
        using var host = CreateHost();
        var siteId = Guid.NewGuid();

        var req = JsonPost("/api/auth/refresh-site", $$"""{ "siteId": "{{siteId}}" }""");
        req.Headers.Add("Cookie", $"{CookieName}={IncomingToken}");
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(IncomingToken, host.Fake.LastRefreshToken);
        Assert.Equal(siteId, host.Fake.LastSiteId);
        var setCookie = string.Join("; ", resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains(RotatedToken, setCookie);
    }

    [Fact]
    public async Task RefreshSite_SiteIdOnlyBody_NoCookie_ReturnsEmptyToken()
    {
        using var host = CreateHost();

        var resp = await host.Client.SendAsync(
            JsonPost("/api/auth/refresh-site", $$"""{ "siteId": "{{Guid.NewGuid()}}" }"""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = ReadJson(resp).RootElement;
        // Action ran (not VALIDATION_FAILED) — no cookie + no body token.
        Assert.Equal("EMPTY_TOKEN", body.GetProperty("errorCode").GetString());
    }

    // ── POST /api/auth/logout — anonymous (expired/absent access token) ──

    [Fact]
    public async Task Logout_NoBearer_WithCookie_ReachesAction_RevokesCookieToken()
    {
        // LOGOUT-RELOGIN regression (2026-09-09): in cookie-only mode the
        // httpOnly cookie is the credential — logout must work when the
        // 15-minute access token is expired/absent. Pre-fix: [Authorize] +
        // DefaultPolicy(RequireAuthenticatedUser) → 401 before the action
        // ran → the cookie was never revoked → the app-boot silent refresh
        // logged the user right back in.
        using var host = CreateHost();

        var req = JsonPost("/api/auth/logout", "{}");
        req.Headers.Add("Cookie", $"{CookieName}={IncomingToken}");
        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // The action ran and handed the COOKIE token to the service.
        Assert.Equal(IncomingToken, host.Fake.LastLogoutToken);
        // The cookie is deleted on the way out (expired Set-Cookie).
        var setCookie = string.Join("; ", resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains(CookieName, setCookie);
    }

    // ── Test double (same shape as the one in AuthControllerCookieTests,
    //    but public-facing so the pipeline DI can use it) ────────────────────

    private sealed class PipelineFakeAuthService : IAuthService
    {
        internal string? LastRefreshToken;
        internal Guid? LastSiteId;
        internal string? LastLogoutToken;

        private static UserProfileDto PlatformProfile() => new(
            Guid.NewGuid(), "admin@syntera.com", "Admin", null,
            "platform", null, null, null,
            new[] { "platform-admin" }, Array.Empty<string>());

        private static UserProfileDto SiteProfile() => new(
            Guid.NewGuid(), "qa@kalbe.co.id", "QA", "QA Officer",
            "site", Guid.NewGuid(), "kalbe", "PT Kalbe",
            new[] { "site-user" }, Array.Empty<string>());

        private static RefreshResponse Refresh(UserProfileDto profile) => new(
            AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RefreshToken: RotatedToken, Profile: profile, Theme: ThemeService.PlatformDefault());

        public Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
            => Task.FromResult(new LoginResponse(
                AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
                RefreshToken: RotatedToken, Profile: PlatformProfile(), Theme: ThemeService.PlatformDefault()));

        public Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
        {
            LastRefreshToken = refreshToken;
            return Task.FromResult(Refresh(PlatformProfile()));
        }

        public Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default)
        {
            LastRefreshToken = refreshToken;
            LastSiteId = siteId;
            return Task.FromResult(Refresh(SiteProfile()));
        }

        public Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
        {
            LastLogoutToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task ChangePasswordAsync(string currentPassword, string newPassword, string? ip, string? userAgent, CancellationToken ct = default, string? passwordChangeChallengeToken = null)
            => Task.CompletedTask;

        public Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default)
            => Task.FromResult(new LoginResponse(
                AccessToken: "access-token-value", ExpiresAt: DateTime.UtcNow.AddMinutes(15),
                RefreshToken: RotatedToken, Profile: PlatformProfile(), Theme: ThemeService.PlatformDefault()));

        public Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default)
            => Task.FromResult(new MfaSetupResponse("otpauth://totp/Test", "JBSWY3DPEHPK3PXP"));

        public Task ConfirmMfaAsync(string code, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisableMfaAsync(string code, CancellationToken ct = default) => Task.CompletedTask;
    }
}
