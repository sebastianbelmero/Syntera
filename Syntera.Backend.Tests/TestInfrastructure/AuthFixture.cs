using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Tests.TestInfrastructure;

/// <summary>
/// SQLite in-memory test fixture for auth flows. Each site id gets its own
/// persistent in-memory database (one open SqliteConnection per site); the
/// platform master DB likewise. All contexts sharing a connection see the
/// same data, which mirrors the production database-per-site topology.
/// </summary>
public sealed class AuthFixture : IDisposable
{
    public const string PlatformAdminEmail = "admin@syntera.com";
    public const string PlatformAdminPassword = "CorrectHorse!42";
    public const string TestSigningKey = "test-signing-key-0123456789abcdefghijklmnopqrstuvwxyz";

    public SqliteConnection PlatformConnection { get; }
    public PlatformDbContext PlatformDb { get; }
    public TestSiteDbContextFactory SiteFactory { get; } = new();
    public FakeLdapClient Ldap { get; } = new();
    public FakeCurrentUserService Current { get; } = new();
    public FakeThemeService Themes { get; } = new();
    public FakePasswordHasher Hasher { get; } = new();
    public FakeTotpService Totp { get; } = new();
    public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());
    public IConfiguration Config { get; }
    public ITokenService Tokens { get; }
    public IAuditService Audit { get; }
    public IPermissionService Permissions { get; } = new PermissionService(new MemoryCache(new MemoryCacheOptions()));
    public IPasswordPolicy PasswordPolicy { get; }
    public AuthService Auth { get; }

    public AuthFixture()
    {
        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = TestSigningKey,
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "1",
            ["Jwt:RefreshTokenDaysPlatform"] = "1",
            ["Jwt:RefreshTokenDaysSite"] = "7",
            ["Session:IdleMinutes"] = "30",
            ["Session:MaxHours"] = "8",
            ["PasswordPolicy:MinLength"] = "12",
            ["PasswordPolicy:MaxLength"] = "256",
            ["PasswordPolicy:RequireUpper"] = "true",
            ["PasswordPolicy:RequireLower"] = "true",
            ["PasswordPolicy:RequireDigit"] = "true",
            ["PasswordPolicy:RequireSymbol"] = "true",
            ["PasswordPolicy:HistorySize"] = "10",
            ["PasswordPolicy:MaxAgeDays"] = "90",
            ["Mfa:Issuer"] = "Syntera IAM Tests",
        }).Build();

        PlatformConnection = new SqliteConnection("DataSource=:memory:");
        PlatformConnection.Open();
        PlatformDb = CreatePlatformContext();
        PlatformDb.Database.EnsureCreated();

        Tokens = new JwtTokenService(Config);
        PasswordPolicy = new PasswordPolicy(Config);
        Audit = new AuditService(PlatformDb, SiteFactory, Current, NullLogger<AuditService>.Instance);

        // REFACTOR (2026-09): the former 14-dependency AuthService is now a
        // facade over four focused services — construct them exactly as the
        // DI container does in Program.cs.
        IRefreshTokenService refreshTokens = new RefreshTokenService(Tokens, Config, Audit);
        IPlatformAdminAuthService platformAuth = new PlatformAdminAuthService(
            PlatformDb, Tokens, Hasher, Audit, Permissions, Cache,
            NullLogger<PlatformAdminAuthService>.Instance, Current, Config, PasswordPolicy, Totp,
            refreshTokens);
        ISiteUserAuthService siteAuth = new SiteUserAuthService(
            PlatformDb, SiteFactory, Ldap, Audit, Themes, Permissions, Cache,
            NullLogger<SiteUserAuthService>.Instance, refreshTokens);
        IRefreshFlowService refreshFlow = new RefreshFlowService(
            PlatformDb, SiteFactory, Audit, Themes, Permissions,
            NullLogger<RefreshFlowService>.Instance, Current, refreshTokens);

        Auth = new AuthService(platformAuth, siteAuth, refreshFlow, Cache);
    }

    private PlatformDbContext CreatePlatformContext()
        => new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlite(PlatformConnection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options);

    /// <summary>Fresh platform context on the same in-memory database (for
    /// verifying persisted state independently of the tracked entities).</summary>
    public PlatformDbContext NewPlatformContext() => CreatePlatformContext();

    // ── Seeding helpers ───────────────────────────────────────────────────

    public PlatformUser SeedPlatformAdmin(
        bool enabled = true,
        bool totpEnabled = false,
        string? totpSecret = null,
        DateTime? passwordChangedAt = null)
    {
        var admin = new PlatformUser
        {
            Email = PlatformAdminEmail,
            PasswordHash = Hasher.Hash(PlatformAdminPassword),
            DisplayName = "Platform Admin",
            IsEnabled = enabled,
            TotpEnabled = totpEnabled,
            TotpSecret = totpSecret,
            PasswordChangedAt = passwordChangedAt ?? DateTime.UtcNow.AddDays(-10),
        };
        PlatformDb.PlatformUsers.Add(admin);
        PlatformDb.SaveChanges();
        return admin;
    }

    public Guid SeedSite(string code, string emailDomain, bool enabled = true)
    {
        var site = new Site
        {
            Code = code,
            DisplayName = $"PT {code.ToUpperInvariant()} Test",
            DatabaseConnectionString = "Data Source=(test);Not Real",
            DefaultThemeKey = "test-default",
            IsEnabled = enabled,
        };
        PlatformDb.Sites.Add(site);
        PlatformDb.LdapDomains.Add(new SiteLdapDomain
        {
            SiteId = site.Id,
            Domain = emailDomain,
            IsActive = true,
        });
        PlatformDb.LdapConfigs.Add(new SiteLdapConfig
        {
            SiteId = site.Id,
            Host = "ldap.test.local",
            Port = 636,
            UseStartTls = false,
            BaseDn = $"DC={code.ToUpperInvariant()},DC=DOM",
            UpnDomain = $"{code}.dom",
        });
        PlatformDb.SaveChanges();
        return site.Id;
    }

    public User SeedSiteUser(Guid siteId, string email, bool enabled = true)
    {
        var ctx = SiteFactory.CreateContext(siteId);
        var user = new User
        {
            Email = email,
            DisplayName = email.Split('@')[0],
            Title = "Tester",
            SiteId = siteId,
            IsEnabled = enabled,
            PermissionsVersion = 1,
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    public void Dispose()
    {
        PlatformDb.Dispose();
        PlatformConnection.Dispose();
        SiteFactory.Dispose();
    }
}

/// <summary>
/// Test ISiteDbContextFactory on SQLite. ResolveForSiteAsync mirrors PRODUCTION
/// semantics: exactly ONE context is cached for the factory lifetime and the
/// siteId argument is IGNORED after the first call (this reproduces the original
/// per-request caching that broke the multi-site scan). CreateForSiteAsync
/// always builds a fresh context on the site's own connection.
/// </summary>
public sealed class TestSiteDbContextFactory : ISiteDbContextFactory, IDisposable
{
    private readonly Dictionary<Guid, SqliteConnection> _connections = new();
    private SiteDbContext? _resolved; // production-like single cached instance
    public int CreateForSiteCallCount { get; private set; }

    public Task<SiteDbContext> ResolveAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Test factory: ResolveAsync requires a JWT site_id claim context.");

    public Task<SiteDbContext> ResolveForSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        if (_resolved is not null) return Task.FromResult(_resolved);
        _resolved = CreateContext(siteId);
        return Task.FromResult(_resolved);
    }

    public Task<SiteDbContext> CreateForSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        CreateForSiteCallCount++;
        return Task.FromResult(CreateContext(siteId));
    }

    /// <summary>Fresh uncached context on the site's shared connection.</summary>
    public SiteDbContext CreateContext(Guid siteId)
    {
        if (!_connections.TryGetValue(siteId, out var conn))
        {
            conn = new SqliteConnection("DataSource=:memory:");
            conn.Open();
            _connections[siteId] = conn;
            using var bootstrap = Build(conn);
            bootstrap.Database.EnsureCreated();
        }
        return Build(conn);
    }

    private static SiteDbContext Build(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlite(conn)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options);

    public void Dispose()
    {
        foreach (var conn in _connections.Values) conn.Dispose();
        _resolved?.Dispose();
    }
}

// ── Fakes ────────────────────────────────────────────────────────────────

public sealed class FakeLdapClient : ILdapClient
{
    /// <summary>Override per test; default accepts any credentials and returns a
    /// valid directory entry.</summary>
    public Func<string, string, LdapAuthResult> Handler { get; set; } =
        (email, _) => new LdapAuthResult(
            IsSuccess: true, Dn: $"CN={email.Split('@')[0]}", Email: email,
            DisplayName: "LDAP Display Name", Title: "QA Officer", ErrorMessage: null, LatencyMs: 3);

    public Task<LdapAuthResult> AuthenticateAsync(LdapEndpoint endpoint, string email, string password, CancellationToken ct = default)
        => Task.FromResult(Handler(email, password));

    public Task<LdapAuthResult> TestConnectionAsync(LdapEndpoint endpoint, string testEmail, string testPassword, CancellationToken ct = default)
        => Task.FromResult(Handler(testEmail, testPassword));
}

public sealed class FakeThemeService : IThemeService
{
    public Task<Models.Dtos.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default)
        => Task.FromResult(ThemeService.PlatformDefault());

    public Task InvalidateCacheAsync(Guid siteId) => Task.CompletedTask;
}

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public Guid? SiteId { get; set; }
    public string? SiteCode { get; set; }
    public string Scope { get; set; } = "anonymous";
    public long? PermissionsVersion { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
    public bool IsInRole(string role) => Roles.Contains(role);
    public bool HasPermission(string permissionKey) => false;
    public bool IsPlatformAdmin { get; set; }
    public bool IsSiteBusinessAdmin { get; set; }
}

/// <summary>Fast deterministic hasher — keeps tests milliseconds-fast vs BCrypt's ~250ms.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"fake-hash:{password}";
    public bool Verify(string password, string hash) => hash == $"fake-hash:{password}";
}

public sealed class FakeTotpService : ITotpService
{
    public string ValidCode { get; set; } = "246810";

    public (string plaintextBase32, string encryptedForDb) GenerateNewSecret()
        => ("JBSWY3DPEHPK3PXP", "enc:JBSWY3DPEHPK3PXP");

    public Task<bool> VerifyCodeAsync(string encryptedSecret, string code, CancellationToken ct = default)
        => Task.FromResult(code == ValidCode);

    public string DecryptSecret(string encryptedSecret) => encryptedSecret;

    public string BuildOtpAuthUrl(string issuer, string accountName, string plaintextBase32Secret)
        => $"otpauth://totp/{issuer}:{accountName}?secret={plaintextBase32Secret}";
}
