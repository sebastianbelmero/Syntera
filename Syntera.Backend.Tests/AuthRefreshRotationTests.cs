using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;
using Syntera.Backend.Tests.TestInfrastructure;

namespace Syntera.Backend.Tests;

/// <summary>
/// Regression suite for the refresh-token lifecycle. Each test maps to a
/// concrete defect found during the 2026-09 review (see FIX markers in
/// AuthService.cs) — several of these FAIL against the pre-fix code:
///
/// <list type="bullet">
///   <item>P0-A — platform login never persisted the refresh token row.</item>
///   <item>P0-B — the multi-site scan in RefreshAsync only ever queried the
///     FIRST site's database (per-request context caching ignored siteId).</item>
///   <item>P1-C — family revocation on reuse was never persisted explicitly
///     (it flushed only as a side effect of the audit write, and was lost
///     when that write failed).</item>
///   <item>P1-D — max-session-age used the CURRENT token's CreatedAt (reset
///     on every rotation) so the 8h limit could never fire.</item>
///   <item>P2 — raw refresh token stored in the DB alongside its hash.</item>
///   <item>37013ca regression — ReplacedById must be set on the OLD token,
///     never on the NEW one (else every 2nd refresh logged the user out).</item>
///   <item>b49aa19 regression — build/verify hash paths must agree (hash is
///     computed over the random part only).</item>
/// </list>
/// </summary>
public sealed class AuthRefreshRotationTests : IDisposable
{
    private readonly AuthFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static async Task<DomainException> ThrowsAuthAsync(Func<Task> act)
    {
        var ex = await Assert.ThrowsAnyAsync<DomainException>(act);
        return ex;
    }

    // ── Platform scope: login persistence (P0-A) ──────────────────────────

    [Fact]
    public async Task PlatformLogin_PersistsRefreshTokenRow()
    {
        _fx.SeedPlatformAdmin();

        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));

        var rows = await _fx.PlatformDb.RefreshTokens.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);

        var admin = await _fx.PlatformDb.PlatformUsers.AsNoTracking().SingleAsync();
        Assert.Equal("platform", row.UserScope);
        Assert.Equal(admin.Id, row.UserId);
        Assert.NotEqual(Guid.Empty, row.FamilyId);
        Assert.NotNull(row.FamilyId);
        Assert.NotNull(row.LastUsedAt);
        Assert.True(row.ExpiresAt > DateTime.UtcNow);
        Assert.Null(row.RevokedAt);
        Assert.Null(row.ReplacedById);

        // FIX (P2): the raw token must NOT be stored — the hash is the only
        // lookup key and the raw value nullifies the hash's DB-read defense.
        Assert.Equal(string.Empty, row.Token);
    }

    [Fact]
    public async Task PlatformLogin_ThenRefresh_Succeeds()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        // Before the P0-A fix this threw REFRESH_NOT_FOUND (token never persisted).
        var refreshed = await _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent");

        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
        Assert.False(string.IsNullOrEmpty(refreshed.RefreshToken));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal("platform", refreshed.Profile.Scope);
        Assert.Contains("platform-admin", refreshed.Profile.Roles);
    }

    // ── Rotation semantics ────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RotationMarksOldToken_KeepsFamily_AndNeverMarksNewToken()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");
        var oldRowId = (await _fx.PlatformDb.RefreshTokens.AsNoTracking().SingleAsync()).Id;
        var familyId = (await _fx.PlatformDb.RefreshTokens.AsNoTracking().SingleAsync()).FamilyId;

        var refreshed = await _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent");

        // Use a fresh context so we read committed DB state, not tracked entities.
        using var db = _fx.NewPlatformContext();
        var rows = await db.RefreshTokens.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();
        Assert.Equal(2, rows.Count);

        var oldRow = rows.Single(r => r.Id == oldRowId);
        var newRow = rows.Single(r => r.Id != oldRowId);

        // OLD token: revoked + replaced by the NEW token's id (commit 37013ca
        // semantics — ReplacedById on the OLD row, never on the NEW row).
        Assert.NotNull(oldRow.RevokedAt);
        Assert.Equal(newRow.Id, oldRow.ReplacedById);

        // NEW token: live, unmarked, same family, LastUsedAt baseline set.
        Assert.Null(newRow.RevokedAt);
        Assert.Null(newRow.ReplacedById);
        Assert.Equal(familyId, newRow.FamilyId);
        Assert.NotNull(newRow.LastUsedAt);

        // Exactly ONE live token in the family after rotation (rotation invariant).
        var live = rows.Count(r => r.RevokedAt == null && r.ReplacedById == null);
        Assert.Equal(1, live);
    }

    [Fact]
    public async Task Refresh_ChainOfThreeRotations_Succeeds()
    {
        // Regression for the original production symptom: after the
        // ReplacedById-on-wrong-row bug, the SECOND refresh always threw
        // REFRESH_REUSE_DETECTED and logged the user out.
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var token = login.RefreshToken;
        var familyId = (await _fx.PlatformDb.RefreshTokens.AsNoTracking().SingleAsync()).FamilyId;

        for (var i = 1; i <= 3; i++)
        {
            var refreshed = await _fx.Auth.RefreshAsync(token, ip: "10.0.0.1", userAgent: "test-agent");
            Assert.False(string.IsNullOrEmpty(refreshed.RefreshToken));
            token = refreshed.RefreshToken;
        }

        using var db = _fx.NewPlatformContext();
        var rows = await db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(4, rows.Count); // login + 3 rotations
        Assert.All(rows, r => Assert.Equal(familyId, r.FamilyId));
        Assert.Single(rows.Where(r => r.RevokedAt == null && r.ReplacedById == null));
    }

    // ── Reuse detection + family revocation (P1-C) ───────────────────────

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_RevokesWholeFamily_Persisted()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var refreshed = await _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent");

        // Replay the OLD (already-rotated) token → theft signal.
        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("REFRESH_REUSE_DETECTED", ex.Code);

        // P1-C: the family revocation must actually be PERSISTED (previously it
        // relied on the audit write to flush it). The CURRENT (legitimate) token
        // must now be revoked in the DB as well — both attacker and victim are
        // forced to re-authenticate.
        using var db = _fx.NewPlatformContext();
        var rows = await db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.RevokedAt));

        // And the previously-valid token is now dead too.
        var ex2 = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(refreshed.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("REFRESH_REUSE_DETECTED", ex2.Code);
    }

    // ── Fast-reject + expiry ─────────────────────────────────────────────

    [Theory]
    [InlineData("garbage-no-signature")]
    [InlineData("onlyrandompart")]
    [InlineData("fake.sig-but-wrong-key-material-aaaaaaaaaaaaaaaaaaaa")]
    public async Task Refresh_ForgedOrMalformedToken_RejectedWithoutLeak(string token)
    {
        _fx.SeedPlatformAdmin();

        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(token, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("REFRESH_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Rejected()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var row = await _fx.PlatformDb.RefreshTokens.SingleAsync();
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _fx.PlatformDb.SaveChangesAsync();

        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("REFRESH_EXPIRED", ex.Code);
    }

    // ── Session limits ───────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_IdleTimeout_RejectsAndRevokes()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var row = await _fx.PlatformDb.RefreshTokens.SingleAsync();
        row.LastUsedAt = DateTime.UtcNow.AddHours(-2); // idle 2h > 30min limit
        await _fx.PlatformDb.SaveChangesAsync();

        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("SESSION_IDLE_TIMEOUT", ex.Code);

        using var db = _fx.NewPlatformContext();
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Refresh_MaxSessionAge_MeasuredFromFamilyRoot_NotCurrentToken()
    {
        // P1-D regression: the max-session check must use the OLDEST row in the
        // family (the login row), not the CURRENT token's CreatedAt — which is
        // reset on every rotation. On the pre-fix code this test FAILS because
        // the 10-hour-old session passes the 8-hour limit.
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.1", userAgent: "test-agent");

        var refreshed = await _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent");

        // Backdate ONLY the family root (the login row) by 10 hours. The current
        // token is minutes old — the idle check must pass, the max-age check
        // must fire on the family root.
        var rootRow = await _fx.PlatformDb.RefreshTokens.AsNoTracking()
            .OrderBy(r => r.CreatedAt).FirstAsync();
        var tracked = await _fx.PlatformDb.RefreshTokens.SingleAsync(r => r.Id == rootRow.Id);
        tracked.CreatedAt = DateTime.UtcNow.AddHours(-10);
        await _fx.PlatformDb.SaveChangesAsync();

        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(refreshed.RefreshToken, ip: "10.0.0.1", userAgent: "test-agent"));
        Assert.Equal("SESSION_EXPIRED", ex.Code);
    }

    // ── Site scope: login + generic refresh via multi-site scan (P0-B) ───

    [Fact]
    public async Task SiteLogin_PersistsRefreshTokenInSiteDb_AndRotationWorks()
    {
        var siteId = _fx.SeedSite("kalventis", "kalventis.test");
        _fx.SeedSiteUser(siteId, "qa.user@kalventis.test");

        var login = await _fx.Auth.LoginAsync(
            new LoginRequest("qa.user@kalventis.test", "whatever-ldap-password"),
            ip: "10.0.0.2", userAgent: "test-agent");

        Assert.Equal("site", login.Profile.Scope);
        Assert.Equal(siteId, login.Profile.SiteId);

        using (var siteDb = _fx.SiteFactory.CreateContext(siteId))
        {
            var row = await siteDb.RefreshTokens.AsNoTracking().SingleAsync();
            Assert.Equal("site", row.UserScope);
            Assert.Equal(siteId, row.SiteId);
            Assert.Equal(string.Empty, row.Token); // P2: raw token not stored
        }

        var refreshed = await _fx.Auth.RefreshSiteAsync(login.RefreshToken, siteId, ip: "10.0.0.2", userAgent: "test-agent");
        Assert.False(string.IsNullOrEmpty(refreshed.RefreshToken));

        using (var siteDb = _fx.SiteFactory.CreateContext(siteId))
        {
            var rows = await siteDb.RefreshTokens.AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Single(rows.Where(r => r.RevokedAt == null && r.ReplacedById == null));
        }
    }

    [Fact]
    public async Task SiteUser_GenericRefresh_ScansAllSites_AndFindsTokenInNonFirstSite()
    {
        // P0-B regression: the generic /api/auth/refresh endpoint scans every
        // enabled site's DB. On the pre-fix code the scan re-used the FIRST
        // site's cached context for every iteration, so a user of the SECOND
        // site got REFRESH_NOT_FOUND on every page reload. This test FAILS
        // against the pre-fix code.
        var siteA = _fx.SeedSite("kalventis", "kalventis.test");
        var siteB = _fx.SeedSite("kalbe", "kalbe.test"); // user belongs to the SECOND site
        _fx.SeedSiteUser(siteB, "operator@kalbe.test");

        var login = await _fx.Auth.LoginAsync(
            new LoginRequest("operator@kalbe.test", "whatever-ldap-password"),
            ip: "10.0.0.3", userAgent: "test-agent");

        // Generic refresh (no siteId — the frontend can't know it after reload).
        var refreshed = await _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.3", userAgent: "test-agent");

        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
        Assert.Equal(siteB, refreshed.Profile.SiteId);

        // The scan must have opened a DISTINCT context per enabled site.
        Assert.Equal(2, _fx.SiteFactory.CreateForSiteCallCount);

        using var siteDb = _fx.SiteFactory.CreateContext(siteB);
        var rows = await siteDb.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count); // login row (rotated) + new row
        Assert.Single(rows.Where(r => r.RevokedAt == null && r.ReplacedById == null));
    }

    // ── MFA login persistence (P0-A) ──────────────────────────────────────

    [Fact]
    public async Task MfaLogin_Flow_RequiresCode_ThenPersistsRefreshToken()
    {
        _fx.SeedPlatformAdmin(totpEnabled: true, totpSecret: "enc:test-secret");

        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.4", userAgent: "test-agent");

        // Step 1: password OK but MFA required — NO tokens issued yet.
        Assert.True(login.RequiresMfa);
        Assert.False(string.IsNullOrEmpty(login.MfaChallengeToken));
        Assert.Equal(string.Empty, login.AccessToken);
        Assert.Equal(string.Empty, login.RefreshToken);
        Assert.Empty(login.Profile.Roles); // minimal profile pre-authentication

        // Wrong TOTP code → INVALID_MFA_CODE.
        var bad = await ThrowsAuthAsync(() => _fx.Auth.LoginMfaAsync(
            new LoginMfaRequest(login.MfaChallengeToken!, "000000"), ip: "10.0.0.4", userAgent: "test-agent"));
        Assert.Equal("INVALID_MFA_CODE", bad.Code);

        // Garbage challenge token → INVALID_MFA_CHALLENGE.
        var badChallenge = await ThrowsAuthAsync(() => _fx.Auth.LoginMfaAsync(
            new LoginMfaRequest("not-a-real-jwt", _fx.Totp.ValidCode), ip: "10.0.0.4", userAgent: "test-agent"));
        Assert.Equal("INVALID_MFA_CHALLENGE", badChallenge.Code);

        // Step 2: correct code → full tokens.
        var mfaLogin = await _fx.Auth.LoginMfaAsync(
            new LoginMfaRequest(login.MfaChallengeToken!, _fx.Totp.ValidCode), ip: "10.0.0.4", userAgent: "test-agent");
        Assert.False(string.IsNullOrEmpty(mfaLogin.AccessToken));
        Assert.False(string.IsNullOrEmpty(mfaLogin.RefreshToken));

        // P0-A: the refresh row MUST be persisted for the MFA path too.
        using var db = _fx.NewPlatformContext();
        var row = Assert.Single(await db.RefreshTokens.AsNoTracking().ToListAsync());
        Assert.Equal("platform", row.UserScope);
        Assert.Null(row.RevokedAt);

        // And the persisted token actually works.
        var refreshed = await _fx.Auth.RefreshAsync(mfaLogin.RefreshToken, ip: "10.0.0.4", userAgent: "test-agent");
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
    }

    // ── Logout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken_SubsequentRefreshRejected()
    {
        _fx.SeedPlatformAdmin();
        var login = await _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, AuthFixture.PlatformAdminPassword),
            ip: "10.0.0.5", userAgent: "test-agent");

        var adminId = (await _fx.PlatformDb.PlatformUsers.AsNoTracking().SingleAsync()).Id;
        await _fx.Auth.LogoutAsync(login.RefreshToken, revokedBy: adminId);

        using var db = _fx.NewPlatformContext();
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt);

        var ex = await ThrowsAuthAsync(() => _fx.Auth.RefreshAsync(login.RefreshToken, ip: "10.0.0.5", userAgent: "test-agent"));
        Assert.Equal("REFRESH_REUSE_DETECTED", ex.Code);
    }

    // ── Login failure paths ──────────────────────────────────────────────

    [Fact]
    public async Task Login_WrongPassword_Rejected_GenericError()
    {
        _fx.SeedPlatformAdmin();

        var ex = await ThrowsAuthAsync(() => _fx.Auth.LoginAsync(
            new LoginRequest(AuthFixture.PlatformAdminEmail, "wrong-password"), ip: "10.0.0.6", userAgent: "test-agent"));
        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
    }

    [Fact]
    public async Task Login_UnknownEmailDomain_Rejected()
    {
        var ex = await ThrowsAuthAsync(() => _fx.Auth.LoginAsync(
            new LoginRequest("user@nowhere.test", "pw"), ip: "10.0.0.6", userAgent: "test-agent"));
        Assert.Equal("DOMAIN_NOT_REGISTERED", ex.Code);
    }

    [Fact]
    public async Task Login_LdapFailure_MappedToGenericMessage()
    {
        var siteId = _fx.SeedSite("kalventis", "kalventis.test");
        _fx.SeedSiteUser(siteId, "qa.user@kalventis.test");
        _fx.Ldap.Handler = (_, _) => new LdapAuthResult(
            IsSuccess: false, Dn: null, Email: null, DisplayName: null, Title: null,
            ErrorMessage: "Bind failed: invalid credentials (49)", LatencyMs: 2);

        var ex = await ThrowsAuthAsync(() => _fx.Auth.LoginAsync(
            new LoginRequest("qa.user@kalventis.test", "bad-password"), ip: "10.0.0.7", userAgent: "test-agent"));
        Assert.Equal("LDAP_AUTH_FAILED", ex.Code);
        // H5: internal LDAP detail must not leak to the caller.
        Assert.Equal("Invalid email or password.", ex.Message);
    }
}
