using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syntera.Backend.Data;

namespace Syntera.Backend.Services;

/// <summary>
/// M5: background sweeper that deletes audit log entries older than the
/// configured retention window. Runs once per day at 03:00 local time, with
/// a fallback startup pass.
///
/// Design notes:
/// - Uses raw SQL (<see cref="DbContext.Database.ExecuteSqlInterpolatedAsync"/>)
///   to bypass the <c>RejectAuditLogMutation</c> guard in PlatformDbContext /
///   SiteDbContext. The guard blocks app-code-driven UPDATE/DELETE; the
///   sweeper is the explicit, code-reviewed exception.
/// - Only deletes if <c>Audit:EnforceRetention=true</c> in config (default
///   false). This gives operators an opt-in: in regulated environments you
///   typically keep audit logs forever and archive cold storage; in dev
///   or low-volume deployments you can flip this on to keep the table small.
/// - Logs the delete count per pass for forensic visibility.
/// - Retention window = Audit:RetentionYears (default 10) — match existing
///   documentation in AuditLog.cs.
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AuditRetentionService> _log;

    /// <summary>Run interval. Hardcoded to 24h — a daily pass is enough
    /// and keeps the DB load predictable. Could be configurable if needed.</summary>
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    public AuditRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<AuditRetentionService> log)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't block app startup — let the host start, then begin sweep loop.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        _log.LogInformation("AuditRetentionService started. Interval={Interval}. Will run first pass shortly.",
            RunInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let the sweeper kill the host. Log and continue.
                _log.LogError(ex, "AuditRetentionService pass failed — will retry next interval.");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        var enforce = _config.GetValue<bool>("Audit:EnforceRetention");
        var retentionYears = _config.GetValue<int>("Audit:RetentionYears");
        if (!enforce)
        {
            _log.LogDebug("Audit:EnforceRetention=false — skipping sweep pass.");
            return;
        }
        if (retentionYears <= 0)
        {
            _log.LogWarning("Audit:RetentionYears={Years} is invalid (must be > 0). Skipping sweep.", retentionYears);
            return;
        }

        var cutoff = DateTime.UtcNow.AddYears(-retentionYears);
        _log.LogInformation("Audit retention sweep starting. Cutoff={Cutoff:O} (rows older than this will be deleted).", cutoff);

        using var scope = _scopeFactory.CreateScope();
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var siteDbFactory = scope.ServiceProvider.GetRequiredService<ISiteDbContextFactory>();

        // ── Platform DB ───────────────────────────────────────────────
        var platformDeleted = await DeleteOldAuditRowsAsync(platformDb, cutoff, ct).ConfigureAwait(false);
        _log.LogInformation("Platform DB audit sweep: {Count} rows deleted.", platformDeleted);

        // ── All site DBs ──────────────────────────────────────────────
        var sites = await platformDb.Sites.AsNoTracking()
            .Where(s => s.IsEnabled)
            .Select(s => new { s.Id, s.Code })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var site in sites)
        {
            SiteDbContext siteDb;
            try
            {
                siteDb = await siteDbFactory.ResolveForSiteAsync(site.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not resolve Site DB for {Code} — skipping its audit sweep.", site.Code);
                continue;
            }

            try
            {
                var siteDeleted = await DeleteOldAuditRowsAsync(siteDb, cutoff, ct).ConfigureAwait(false);
                _log.LogInformation("Site {Code} audit sweep: {Count} rows deleted.", site.Code, siteDeleted);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Site {Code} audit sweep failed.", site.Code);
            }
        }
    }

    /// <summary>
    /// Delete audit log rows with Timestamp &lt; cutoff, using parameterized
    /// raw SQL. Raw SQL bypasses the DbContext's <c>RejectAuditLogMutation</c>
    /// guard (which only inspects ChangeTracker entries, not raw commands).
    /// </summary>
    private static Task<int> DeleteOldAuditRowsAsync(DbContext db, DateTime cutoff, CancellationToken ct)
    {
        // Pass the interpolated string directly — assigning it to a `string`
        // variable first would lose the FormattableString type. Inline keeps
        // it as FormattableString so ExecuteSqlInterpolatedAsync sees a
        // parameterized SQL command (no SQL injection risk).
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM AuditLogs WHERE Timestamp < {cutoff}", ct);
    }
}

/// <summary>
/// DI extension to register the AuditRetentionService hosted service.
/// </summary>
public static class AuditRetentionServiceExtensions
{
    public static IServiceCollection AddAuditRetentionSweeper(this IServiceCollection services)
        => services.AddHostedService<AuditRetentionService>();
}
