using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Syntera.Infrastructure.Data;

namespace Syntera.Api.Factories;

/// <summary>
/// Design-time factory for PlatformDbContext. Used by `dotnet ef migrations
/// add ... --context PlatformDbContext` so the EF tools can construct the
/// context without running the full application startup.
///
/// Migrations live in <c>Migrations/Platform/</c> subdirectory with namespace
/// <c>Syntera.Api.Migrations.Platform</c>. The MigrationsAssembly call below
/// tells EF where to look for the migration history table at runtime.
/// </summary>
public sealed class PlatformDbContextDesignFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "SYNTERA_")
            .Build();

        var conn = config.GetConnectionString("Platform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Platform not found. Set it in appsettings.Development.json " +
                "or via SYNTERA_ConnectionStrings__Platform env var.");

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(conn, sql => sql
                .MigrationsAssembly(typeof(PlatformDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory_Platform"))
            .Options;

        return new PlatformDbContext(options);
    }
}

/// <summary>
/// Design-time factory for SiteDbContext. The site DB schema is identical
/// across all 6+ sites — one migration set covers all of them. At design
/// time we point at a dummy connection string (the migration only needs
/// the model, not a live connection).
///
/// Usage:
///   dotnet ef migrations add InitialSite --context SiteDbContext --output-dir Migrations/Site
/// </summary>
public sealed class SiteDbContextDesignFactory : IDesignTimeDbContextFactory<SiteDbContext>
{
    public SiteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=syntera_site_design;User Id=sa;Password=design_only;TrustServerCertificate=True",
                sql => sql
                    .MigrationsAssembly(typeof(SiteDbContext).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory_Site"))
            .Options;

        return new SiteDbContext(options);
    }
}
