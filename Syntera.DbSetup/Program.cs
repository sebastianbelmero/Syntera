using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Syntera.Backend.Data;

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
    Log.Information("▶ Step 3/3: Seed platform data (admin user, role templates, 6 sites, themes)");
    Log.Information("");

    using (var platformDb = new PlatformDbContext(platformOptions))
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
        var logger = loggerFactory.CreateLogger("DbSeeder");

        await DbSeeder.SeedPlatformAsync(platformDb, config, logger);
        Log.Information("  ✓ Seeding complete");
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
/// </summary>
static void EnsureDatabaseExists(string connStr, string dbName)
{
    Log.Information("  Ensuring database '{Db}' exists...", dbName);

    var builder = new SqlConnectionStringBuilder(connStr);
    var serverConnStr = new SqlConnectionStringBuilder
    {
        DataSource = builder.DataSource,
        UserID = builder.UserID,
        Password = builder.Password,
        InitialCatalog = "master",
        TrustServerCertificate = builder.TrustServerCertificate,
        ConnectTimeout = 10,
    }.ConnectionString;

    using var conn = new SqlConnection(serverConnStr);
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
