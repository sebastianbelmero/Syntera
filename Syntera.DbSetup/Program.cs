using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Syntera.Backend.Data;
using Syntera.Backend.Services;

// ─── Bootstrap Serilog (matches API style) ─────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("════════════════════════════════════════════════════════════════");
    Log.Information("  Syntera DbSetup — creating all databases & applying migrations");
    Log.Information("════════════════════════════════════════════════════════════════");

    // ─── Load configuration (same files as the API) ───────────────────
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables(prefix: "SYNTERA_")
        .Build();

    var platformConn = config.GetConnectionString("Platform")
        ?? throw new InvalidOperationException("ConnectionStrings:Platform is missing from appsettings.json");

    var siteConns = config.GetSection("ConnectionStrings:Sites").Get<Dictionary<string, string>>()
        ?? new Dictionary<string, string>();

    if (siteConns.Count == 0)
        Log.Warning("No site connection strings found in ConnectionStrings:Sites. Only platform DB will be set up.");

    // ─── Step 1: Create Platform DB + apply migrations ───────────────
    Log.Information("");
    Log.Information("▶ Step 1/3: Platform database (syntera_master)");
    Log.Information("");

    EnsureDatabaseExists(platformConn, "syntera_master");

    var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
        .UseSqlServer(platformConn, sql => sql
            .MigrationsAssembly(typeof(PlatformDbContext).Assembly.FullName)
            .MigrationsHistoryTable("__EFMigrationsHistory_Platform"))
        .Options;

    using (var platformDb = new PlatformDbContext(platformOptions))
    {
        Log.Information("  Applying PlatformDbContext migrations...");
        await platformDb.Database.MigrateAsync();
        Log.Information("  ✓ Platform migrations applied");
    }

    // ─── Step 2: Create all site DBs + apply SiteDbContext migrations ─
    Log.Information("");
    Log.Information("▶ Step 2/3: Site databases ({Count} sites)", siteConns.Count);
    Log.Information("");

    foreach (var (siteCode, siteConn) in siteConns)
    {
        var dbName = ExtractDatabaseName(siteConn) ?? $"syntera_{siteCode}";
        Log.Information("  [{Code}]", siteCode.ToUpperInvariant());

        EnsureDatabaseExists(siteConn, dbName);

        var siteOptions = new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlServer(siteConn, sql => sql
                .MigrationsAssembly(typeof(SiteDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory_Site"))
            .Options;

        using var siteDb = new SiteDbContext(siteOptions);
        Log.Information("    Applying SiteDbContext migrations...");
        await siteDb.Database.MigrateAsync();
        Log.Information("    ✓ {Code} migrations applied", siteCode);
    }

    // ─── Step 3: Seed platform data ──────────────────────────────────
    Log.Information("");
    Log.Information("▶ Step 3/3: Seed platform data + compliance schema migration");
    Log.Information("");

    using (var platformDb = new PlatformDbContext(platformOptions))
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
        var logger = loggerFactory.CreateLogger("DbSeeder");

        // COMPLIANCE (Sprint 2.6): set up a minimal DI container so we can
        // resolve IConnectionStringProtector (which depends on
        // IDataProtectionProvider). When ConnectionProtection:Enabled=true
        // in config, the seeder will encrypt connection strings at rest.
        // When false (dev default), the protector is a no-op.
        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeyPath"] ?? "./keys"));
        // ConnectionStringProtector needs both IDataProtectionProvider
        // (auto-registered by AddDataProtection) and IConfiguration.
        // Singleton is fine — DbSetup is single-threaded.
        services.AddSingleton<IConfiguration>(_ => config);
        services.AddSingleton<IConnectionStringProtector, ConnectionStringProtector>();
        var sp = services.BuildServiceProvider();
        var protector = sp.GetRequiredService<IConnectionStringProtector>();

        await DbSeeder.SeedPlatformAsync(platformDb, config, logger, protector);

        // COMPLIANCE (Sprint 2.2): apply compliance schema additions (MFA,
        // PasswordHistory, RefreshToken.LastUsedAt, AuditLog.SignatureMeaning,
        // RoleTemplateApprovals) to Platform DB + all enabled site DBs.
        // Idempotent — safe to run on every invocation.
        Log.Information("  Applying compliance schema additions (MFA, PasswordHistory, etc.) to Platform DB...");
        await ComplianceMigrator.ApplyPlatformAsync(platformDb, logger);

        foreach (var (siteCode, siteConn) in siteConns)
        {
            Log.Information("  Applying compliance schema to site {Code}...", siteCode);
            var siteOpts = new DbContextOptionsBuilder<SiteDbContext>()
                .UseSqlServer(siteConn, sql => sql
                    .MigrationsAssembly(typeof(SiteDbContext).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory_Site"))
                .Options;
            using var siteDb = new SiteDbContext(siteOpts);
            await ComplianceMigrator.ApplySiteAsync(siteDb, logger);
        }

        Log.Information("  ✓ Seeding + compliance migration complete");
    }

    // ─── Summary ─────────────────────────────────────────────────────
    Log.Information("");
    Log.Information("════════════════════════════════════════════════════════════════");
    Log.Information("  ✓ All databases ready!");
    Log.Information("");
    Log.Information("  Platform DB: syntera_master ({Count} tables)",
        CountTables(platformConn, "syntera_master"));

    foreach (var (siteCode, siteConn) in siteConns)
    {
        var dbName = ExtractDatabaseName(siteConn) ?? $"syntera_{siteCode}";
        Log.Information("  Site DB {Code,-10}: {Db} ({Count} tables)",
            siteCode, dbName, CountTables(siteConn, dbName));
    }

    Log.Information("");
    Log.Information("  Platform Admin: admin@syntera.com");
    Log.Information("  Password:       (from Seed:PlatformAdminPassword in appsettings)");
    Log.Information("");
    Log.Information("  Next: cd ../Syntera.Backend && dotnet run");
    Log.Information("════════════════════════════════════════════════════════════════");
}
catch (Exception ex)
{
    Log.Fatal(ex, "DbSetup failed");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// ─── Helpers ────────────────────────────────────────────────────────

/// <summary>
/// Creates the database if it doesn't exist. Connects to the server (using
/// the master database) and runs CREATE DATABASE. Idempotent.
///
/// <para><b>BUG FIX (Network protocol preservation):</b> the previous
/// implementation rebuilt the connection string via a second
/// <see cref="SqlConnectionStringBuilder"/>, which <b>strips the protocol
/// prefix</b> (e.g., <c>tcp:</c> or <c>np:</c>) from <c>DataSource</c>.
/// On Windows, the resulting connection fell back to Shared Memory /
/// Named Pipes — and timed out (Win32 258) when the SQL Server doesn't
/// have those protocols enabled. SSMS works because it preserves the
/// protocol prefix from the user's connection string.</para>
///
/// <para><b>Fix:</b> instead of rebuilding the connection string, do a
/// string <c>Replace</c> on the original (preserving the protocol prefix
/// and every other setting) — only swap <c>Database=X</c> to
/// <c>Database=master</c>. This keeps <c>tcp:localhost</c> intact so
/// SqlClient connects via TCP/IP as intended.</para>
/// </summary>
static void EnsureDatabaseExists(string connStr, string dbName)
{
    Log.Information("  Ensuring database '{Db}' exists...", dbName);

    // DEBUG: log a sanitized view of the connection string so we can see
    // exactly what EnsureDatabaseExists is sending to SqlClient. We hide
    // any Password=... value to avoid leaking credentials in logs.
    // This is critical for diagnosing protocol/instance-resolution issues.
    var sanitized = System.Text.RegularExpressions.Regex.Replace(
        connStr,
        @"(Password|Pwd)\s*=\s*[^;]+",
        "$1=***REDACTED***",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    Log.Information("  [DEBUG] Original connection string: {Conn}", sanitized);

    // Parse to discover the original Initial Catalog (so the Replace
    // is exact, not a guess). SqlConnectionStringBuilder is read-only
    // here — we never use it to rebuild the connection string.
    var builder = new SqlConnectionStringBuilder(connStr);
    var originalDb = builder.InitialCatalog ?? string.Empty;
    Log.Information("  [DEBUG] Parsed DataSource='{DataSource}', OriginalCatalog='{Catalog}'",
        builder.DataSource, originalDb);

    // Preserve EVERYTHING in the original connection string — only swap
    // the database name. This keeps any protocol prefix (tcp:, np:, lpc:)
    // intact so SqlClient uses the protocol the operator specified.
    var masterConnStr = string.IsNullOrEmpty(originalDb)
        ? connStr + (connStr.EndsWith(';') ? "" : ";") + "Database=master"
        : connStr.Replace($"Database={originalDb}", "Database=master", StringComparison.OrdinalIgnoreCase);

    var sanitizedMaster = System.Text.RegularExpressions.Regex.Replace(
        masterConnStr,
        @"(Password|Pwd)\s*=\s*[^;]+",
        "$1=***REDACTED***",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    Log.Information("  [DEBUG] Master connection string: {Conn}", sanitizedMaster);

    using var conn = new SqlConnection(masterConnStr);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"""
        IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{dbName}')
        BEGIN
            CREATE DATABASE [{dbName}];
            PRINT 'Created database {dbName}';
        END
        ELSE
            PRINT 'Database {dbName} already exists';
        """;
    cmd.ExecuteNonQuery();
    Log.Information("  ✓ Database '{Db}' ready", dbName);
}

/// <summary>
/// Extracts the Initial Catalog (database name) from a SQL Server
/// connection string.
/// </summary>
static string? ExtractDatabaseName(string connStr)
{
    try
    {
        var b = new SqlConnectionStringBuilder(connStr);
        return string.IsNullOrWhiteSpace(b.InitialCatalog) ? null : b.InitialCatalog;
    }
    catch
    {
        return null;
    }
}

/// <summary>Counts user tables (excluding __EFMigrationsHistory) in a database.</summary>
static int CountTables(string connStr, string dbName)
{
    try
    {
        using var conn = new SqlConnection(connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE LEFT(name, 2) <> '__'";
        return (int)cmd.ExecuteScalar()!;
    }
    catch
    {
        return -1;
    }
}
