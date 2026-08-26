using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Serilog;
using Syntera.Api.Extensions;
using Syntera.Api.Middleware;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Seed;

// ─── Bootstrap Serilog (early-stage logs go to console) ─────────────
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default ASP.NET Core logging with Serilog
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

    // Layered DI registration — each extension owns its slice.
    builder.Services.AddSynteraPersistence(builder.Configuration);
    builder.Services.AddSynteraIdentity(builder.Configuration);
    builder.Services.AddSynteraApplication();
    builder.Services.AddSynteraOpenApi();
    builder.Services.AddSynteraSecurity(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks()
        .AddSqlServer(
            connectionString: builder.Configuration.GetConnectionString("Default")!,
            name: "sqlserver",
            tags: new[] { "db", "sqlserver" });

    var app = builder.Build();

    // ─── Pipeline ───────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Syntera API v1");
            options.RoutePrefix = "docs";
        });
    }

    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new()
    {
        Predicate = h => h.Tags.Contains("db"),
    });

    // ─── Seed on startup ────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();

        var seedSample = app.Configuration.GetValue("SEED_SAMPLE_DATA", defaultValue: false);
        var adminEmail = app.Configuration["Seed:AdminEmail"] ?? "admin@syntera.local";
        var adminPassword = app.Configuration["Seed:AdminPassword"] ?? "ChangeMe!Strong#1";

        await DbSeeder.SeedAsync(db, users, roles, seedSample, adminEmail, adminPassword);
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

// Marker so Program.cs can be referenced from the tests project.
public partial class Program { }
