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

    // ─── DI: DbContexts ─────────────────────────────────────────────
    builder.Services.AddDbContext<PlatformDbContext>(opt =>
        opt.UseSqlServer(
            builder.Configuration.GetConnectionString("Platform")
                ?? throw new InvalidOperationException("ConnectionStrings:Platform is required."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Platform")));

    builder.Services.AddScoped<Syntera.Backend.Data.ISiteDbContextFactory, SiteDbContextFactory>();

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

    // ─── Database init ───────────────────────────────────────────
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
