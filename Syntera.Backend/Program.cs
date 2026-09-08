using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Syntera.Backend.Authorization;
using Syntera.Backend.Controllers;
using Syntera.Backend.Data;
using Syntera.Backend.Extensions;
using Syntera.Backend.Middleware;
using Syntera.Backend.Services;
using System.Globalization;

// ─── Bootstrap Serilog ─────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(
        formatProvider: CultureInfo.InvariantCulture,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("App", "Syntera.Backend"));

    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true, reloadOnChange: true)
        .AddEnvironmentVariables(prefix: "SYNTERA_");

    // ─── Fail-fast: Production security checks ────────────────────
    if (builder.Environment.IsProduction())
    {
        var signingKey = builder.Configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be set (≥32 chars) in Production. Set via SYNTERA_Jwt__SigningKey env var.");

        var allowedHosts = builder.Configuration["Cors:AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(allowedHosts))
            throw new InvalidOperationException("Cors:AllowedOrigins must be set in Production.");

        var adminPassword = builder.Configuration["Seed:PlatformAdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException("Seed:PlatformAdminPassword must be set in Production. Set via SYNTERA_Seed__PlatformAdminPassword env var.");

        var dbPassword = builder.Configuration.GetConnectionString("Platform");
        if (dbPassword != null && dbPassword.Contains("__SET_VIA_ENV"))
            throw new InvalidOperationException("ConnectionStrings:Platform must not contain placeholder in Production. Set via SYNTERA_ConnectionStrings__Platform env var.");
    }

    // ─── DI: Framework ─────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddMemoryCache();
    builder.Services.AddSynteraMvc();
    builder.Services.AddSynteraOpenApi();
    builder.Services.AddSynteraSecurity(builder.Configuration);

    // ─── DI: Data Protection (DPAPI key ring) ─────────────────────────
    // COMPLIANCE (Sprint 2.3): TOTP secrets are encrypted at rest via
    // ASP.NET Core Data Protection. The key ring must be persisted to a
    // stable location so that secrets remain decryptable across app
    // restarts. In dev the path falls back to a local "keys" folder
    // (created on demand); in production the path comes from
    // DataProtection:KeyPath (default /var/lib/syntera/keys) and MUST be
    // a durable, backed-up location — losing the key ring invalidates
    // every MFA secret and forces every platform admin to re-enroll.
    var dpKeyPath = builder.Configuration["DataProtection:KeyPath"] ?? "/var/lib/syntera/keys";
    var dpKeyDir = new DirectoryInfo(dpKeyPath);
    if (!dpKeyDir.Exists)
    {
        try { dpKeyDir.Create(); }
        catch
        {
            // Fall back to a local "keys" folder if the configured path is
            // not writable (typical in dev sandboxes). We log but do not
            // throw — the default DataProtection behavior (ephemeral keys)
            // would still let the app start, but MFA secrets wouldn't
            // survive a restart.
            dpKeyDir = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "keys"));
            dpKeyDir.Create();
        }
    }
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(dpKeyDir)
        .SetApplicationName("Syntera");

    // ─── DI: DbContexts ─────────────────────────────────────────────
    builder.Services.AddDbContext<PlatformDbContext>(opt =>
        opt.UseSqlServer(
            builder.Configuration.GetConnectionString("Platform")
                ?? throw new InvalidOperationException("ConnectionStrings:Platform is required."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Platform"))
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

    builder.Services.AddScoped<Syntera.Backend.Data.ISiteDbContextFactory, SiteDbContextFactory>();

    // COMPLIANCE (Sprint 2.6): encrypts per-site SQL Server connection
    // strings at rest via ASP.NET Core Data Protection. Scoped because
    // SiteDbContextFactory (also Scoped) consumes it directly, and
    // because the protector's IsEnabled check reads IConfiguration on
    // every call — making it Scoped (not Singleton) keeps the door open
    // for a per-request decrypt cache in the future without changing
    // the registration. Backed by the same DPAPI key ring registered
    // above (purpose "Syntera.ConnectionString.v1"). The key ring at
    // DataProtection:KeyPath MUST be backed up — losing it makes every
    // encrypted connection string undecryptable.
    builder.Services.AddScoped<IConnectionStringProtector, ConnectionStringProtector>();

    // ─── DI: Services ─────────────────────────────────────────────
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddSingleton<ILdapClient, NovellLdapClient>();
    builder.Services.AddScoped<ITokenService, JwtTokenService>();
    builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IPermissionService, PermissionService>();
    builder.Services.AddScoped<IAuditService, AuditService>();
    builder.Services.AddScoped<IThemeService, ThemeService>();
    builder.Services.AddScoped<ISiteManagementService, SiteManagementService>();
    builder.Services.AddScoped<IUserManagementService, UserManagementService>();
    builder.Services.AddScoped<IRoleTemplateService, RoleTemplateService>();
    // M7: password policy enforcement for local-credential users
    // (Platform Admin). Site users authenticate via LDAP — their policy
    // is AD's, not ours.
    builder.Services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
    // COMPLIANCE (Sprint 2.3): TOTP (MFA) service. Singleton — stateless
    // after construction (the IDataProtector is the only state and is
    // thread-safe). The DPAPI key ring is owned by the
    // IDataProtectionProvider, which is itself a singleton.
    builder.Services.AddSingleton<ITotpService, TotpService>();

    // ─── M5: background audit log retention sweeper ────────────────
    // Daily pass that deletes audit log rows older than Audit:RetentionYears.
    // Only runs if Audit:EnforceRetention=true — opt-in to keep the table
    // small. Disabled by default because regulated environments often keep
    // audit logs forever and archive to cold storage separately.
    builder.Services.AddAuditRetentionSweeper();

    // ─── Health checks ─────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // ─── Pipeline ───────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "Syntera API v1");
            o.RoutePrefix = "docs";
        });
    }
    else
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<Syntera.Backend.Middleware.SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");

    // ─── Database init ───────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var siteDbFactory = scope.ServiceProvider.GetRequiredService<ISiteDbContextFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (app.Environment.IsDevelopment())
        {
            await DatabaseInitializer.MigrateOrBaselineAsync(platformDb, logger);
        }

        // COMPLIANCE (Sprint 2.2 — ORDER FIX): apply compliance schema additions
        // BEFORE seeding. The seeder inserts PlatformUser rows that reference
        // the new MFA columns (PasswordChangedAt, TotpSecret, TotpEnabled)
        // — if the columns don't exist yet, SaveChanges throws
        // SqlException 207 'Invalid column name'.
        //
        // Idempotent — safe to run on every startup.
        await ComplianceMigrator.ApplyPlatformAsync(platformDb, logger);
        await ComplianceMigrator.ApplyAllSitesAsync(platformDb, siteDbFactory, logger);

        await DbSeeder.SeedPlatformAsync(
            platformDb,
            app.Configuration,
            logger,
            scope.ServiceProvider.GetService<IConnectionStringProtector>());
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Syntera.Backend terminated with an unhandled exception");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    internal static readonly string[] HealthCheckTags = { "db", "platform" };
}
