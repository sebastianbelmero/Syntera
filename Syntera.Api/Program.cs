using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Syntera.Api.Extensions;
using Syntera.Api.Middleware;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Identity;
using Syntera.Infrastructure.Ldap;
using Syntera.Infrastructure.Seed;
using System.Globalization;

// ─── Bootstrap Serilog (early-stage logs go to console) ─────────────
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(
        formatProvider: CultureInfo.InvariantCulture,
        outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("App", "Syntera.Api"));

    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true, reloadOnChange: true)
        .AddUserSecrets<Program>(optional: true, reloadOnChange: true)
        .AddEnvironmentVariables(prefix: "SYNTERA_");

    // ─── Fail-fast: refuse to start in Production with insecure defaults ─
    if (builder.Environment.IsProduction())
    {
        var signingKey = builder.Configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be set (≥32 chars) in Production.");

        var allowedHosts = builder.Configuration["Cors:AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(allowedHosts))
            throw new InvalidOperationException("Cors:AllowedOrigins must be set in Production.");

        if (builder.Configuration["Ldap:AllowPlain"] == "true")
            throw new InvalidOperationException("Ldap:AllowPlain=true is forbidden in Production.");
    }

    // ─── DI: framework ──────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddMemoryCache();
    builder.Services.AddSynteraMvc();
    builder.Services.AddSynteraOpenApi();
    builder.Services.AddSynteraSecurity(builder.Configuration);

    // ─── DI: DbContexts ─────────────────────────────────────────────
    builder.Services.AddDbContext<PlatformDbContext>(opt =>
        opt.UseSqlServer(
            builder.Configuration.GetConnectionString("Platform")
                ?? throw new InvalidOperationException("ConnectionStrings:Platform is required."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Platform")));

    builder.Services.AddScoped<ISiteDbContextFactory, SiteDbContextFactory>();

    // ─── DI: Identity & current user ────────────────────────────────
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

    // ─── DI: LDAP ───────────────────────────────────────────────────
    builder.Services.AddSingleton<ILdapClient, NovellLdapClient>();

    // ─── DI: Application services ───────────────────────────────────
    builder.Services.AddScoped<ITokenService, JwtTokenService>();
    builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IPermissionService, PermissionService>();
    builder.Services.AddScoped<IAuditService, AuditService>();
    builder.Services.AddScoped<IThemeService, ThemeService>();
    builder.Services.AddScoped<ISiteManagementService, SiteManagementService>();
    builder.Services.AddScoped<IUserManagementService, UserManagementService>();
    builder.Services.AddScoped<IRoleTemplateService, RoleTemplateService>();

    // ─── Health checks ──────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddSqlServer(
            connectionString: builder.Configuration.GetConnectionString("Platform")!,
            name: "platform-db",
            tags: HealthCheckTags);

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
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health");

    // ─── Database init ─────────────────────────────────────────────
    // In Development: auto-migrate + seed so `dotnet run` "just works".
    // Databases created by the pre-migration IAM builds (EnsureCreated
    // era) carry the full schema but no migration history — DatabaseInitializer
    // baselines them instead of replaying the initial migration (which would
    // crash on the already-existing tables).
    // In Production: migrations must be applied via CLI before startup;
    // we only seed (idempotent — safe to call on every boot).
    using (var scope = app.Services.CreateScope())
    {
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (app.Environment.IsDevelopment())
        {
            await DatabaseInitializer.MigrateOrBaselineAsync(platformDb, logger);
        }

        await DbSeeder.SeedPlatformAsync(platformDb, app.Configuration, logger);
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Syntera.Api terminated with an unhandled exception");
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
