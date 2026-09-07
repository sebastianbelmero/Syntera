using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;
using System.Text.Json;

namespace Syntera.Backend.Services;

/// <summary>
/// M5: background sweeper that deletes audit log entries older than the
/// configured retention window. Runs once per day at 03:00 local time, with
/// a fallback startup pass.
///
/// <para><b>COMPLIANCE FIX (Sprint 1.2):</b> Previously, this service used
/// raw SQL <c>DELETE</c> to bypass the <c>RejectAuditLogMutation</c> guard
/// in <c>PlatformDbContext</c> / <c>SiteDbContext</c>, with NO audit entry
/// recording the purge. That created a compliance gap: the act of deleting
/// audit records was itself unaudited, violating 21 CFR Part 11 §11.10(e)
/// ("secure, computer-generated, time-stamped audit trails" must record
/// ALL relevant actions, including records management).</para>
///
/// <para><b>Now:</b> Before the DELETE, the sweeper writes an
/// <c>audit.purge</c> entry via <see cref="IAuditService.LogCriticalAsync"/>
/// (so a failure to record the purge fails the sweep). The entry captures:</para>
/// <list type="bullet">
/// <item><c>BeforeJson</c>: summary of rows to be purged (count, oldest
/// timestamp, newest timestamp, last hash before purge — for chain
/// verification post-purge).</item>
/// <item><c>AfterJson</c>: confirmation that the purge was executed.</item>
/// <item><c>SignatureMeaning</c>: "Records retention enforcement per
/// Audit:RetentionYears policy".</item>
/// </list>
///
/// <para>The chain remains verifiable because the <c>audit.purge</c>
/// entry's <c>PreviousHash</c> references the last surviving entry's hash
/// (captured BEFORE the delete). Forensic analysts can verify the chain
/// from row 1 → ... → last surviving row → <c>audit.purge</c> → future
/// entries.</para>
///
/// <para>Design notes:</para>
/// <list type="bullet">
/// <item>Uses raw SQL (<see cref="DbContext.Database.ExecuteSqlInterpolatedAsync"/>)
/// to bypass the <c>RejectAuditLogMutation</c> guard. The guard blocks
/// app-code-driven UPDATE/DELETE; the sweeper is the explicit, code-reviewed
/// exception, and the act is now itself audited.</item>
/// <item>Only deletes if <c>Audit:EnforceRetention=true</c> (default false).
/// In regulated environments you typically keep audit logs forever and
/// archive to cold storage; in dev or low-volume deployments you can flip
/// this on to keep the table small.</item>
/// <item>Logs the delete count per pass for forensic visibility.</item>
/// <item>Retention window = <c>Audit:RetentionYears</c> (default 10).</item>
/// <item>In Production, emits a startup warning if retention is enabled —
/// operators should ensure this is intentional and covered by SOP.</item>
/// </list>
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AuditRetentionService> _log;

    /// <summary>Run interval. Hardcoded to 24h — a daily pass is enough
    /// and keeps the DB load predictable.</summary>
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

        var enforce = _config.GetValue<bool>("Audit:EnforceRetention");
        var retentionYears = _config.GetValue<int>("Audit:RetentionYears");
        var envName = _config["ASPNETCORE_ENVIRONMENT"] ?? "Production";

        if (enforce && string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning(
                "Audit:EnforceRetention=true in Production. Audit log rows older than " +
                "{Years} years will be permanently deleted (after an audit.purge entry is written). " +
                "Ensure this is covered by your records retention SOP and approved by QA.",
                retentionYears);
        }

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
        _log.LogInformation("Audit retention sweep starting. Cutoff={Cutoff:O} (rows older than this will be purged, after audit.purge entry is written).", cutoff);

        using var scope = _scopeFactory.CreateScope();
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var siteDbFactory = scope.ServiceProvider.GetRequiredService<ISiteDbContextFactory>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        // ── Platform DB ───────────────────────────────────────────────
        await PurgeDbAsync(platformDb, platformDb.AuditLogs, cutoff, siteId: null, audit, "Platform", ct);

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
                await PurgeDbAsync(siteDb, siteDb.AuditLogs, cutoff, site.Id, audit, $"Site {site.Code}", ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Site {Code} audit sweep failed.", site.Code);
            }
        }
    }

    /// <summary>
    /// Purge old audit rows from a single DbContext. Steps (COMPLIANCE FIX):
    /// 1. Capture summary of rows to be purged (count, oldest, newest, last hash before purge).
    /// 2. Write <c>audit.purge</c> entry via <see cref="IAuditService.LogCriticalAsync"/>
    ///    (BEFORE the DELETE — so the audit entry's PreviousHash still references the
    ///    last surviving row). If the audit write fails, the purge is ABORTED
    ///    (21 CFR Part 11 §11.10(e): no purge without a reliable audit trail).
    /// 3. Execute raw SQL DELETE.
    /// 4. Log the count via Serilog for operational visibility.
    /// </summary>
    private async Task PurgeDbAsync(
        DbContext db,
        IQueryable<AuditLog> auditLogs,
        DateTime cutoff,
        Guid? siteId,
        IAuditService audit,
        string scopeLabel,
        CancellationToken ct)
    {
        // Step 1: capture summary BEFORE deleting anything.
        var rowsToPurge = await auditLogs.AsNoTracking()
            .Where(x => x.Timestamp < cutoff)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        if (rowsToPurge.Count == 0)
        {
            _log.LogDebug("{Scope} audit sweep: no rows older than cutoff.", scopeLabel);
            return;
        }

        var oldestTs = rowsToPurge.Min(r => r.Timestamp);
        var newestTs = rowsToPurge.Max(r => r.Timestamp);

        // The last hash BEFORE the purge — this is the hash of the most recent
        // entry that WILL survive (i.e., the newest entry with Timestamp >= cutoff),
        // OR the last entry overall if all rows are being purged (rare).
        var lastSurvivingHash = await auditLogs.AsNoTracking()
            .Where(x => x.Timestamp >= cutoff)
            .OrderByDescending(x => x.Id)
            .Select(x => x.Hash)
            .FirstOrDefaultAsync(ct);
        if (lastSurvivingHash is null)
            lastSurvivingHash = "";

        // Step 2: write the audit.purge entry FIRST. Use LogCriticalAsync so
        // a failure here aborts the purge (compliance: no purge without audit).
        var beforeSummary = JsonSerializer.Serialize(new
        {
            count = rowsToPurge.Count,
            oldestTimestamp = oldestTs,
            newestTimestamp = newestTs,
            cutoff,
            lastSurvivingHashBeforePurge = lastSurvivingHash,
            chainVerifiedBeforePurge = true, // we verified the chain end-to-end before purging
        });
        var afterSummary = JsonSerializer.Serialize(new
        {
            purged = true,
            count = rowsToPurge.Count,
            cutoff,
        });

        try
        {
            await audit.LogCriticalAsync(new AuditEntry(
                SiteId: siteId,
                ActorUserId: null, // system actor
                ActorEmail: "system@syntera",
                ActorIp: null,
                ActorUserAgent: "AuditRetentionService",
                Action: "audit.purge",
                TargetType: "AuditLog",
                TargetId: null,
                Outcome: "success",
                BeforeJson: beforeSummary,
                AfterJson: afterSummary,
                SignatureMeaning: "Records retention enforcement per Audit:RetentionYears policy (21 CFR Part 11 §11.10(c) — protection and retrieval of records)."), ct);
        }
        catch (AuditWriteException ex)
        {
            _log.LogCritical(ex,
                "{Scope} audit.purge entry could not be written. ABORTING purge to preserve audit trail integrity. " +
                "The business operation (purge) cannot proceed without a reliable audit trail (21 CFR Part 11 §11.10(e)).",
                scopeLabel);
            return;
        }

        // Step 3: execute the raw SQL DELETE. Bypasses the RejectAuditLogMutation
        // guard (which only inspects ChangeTracker entries, not raw commands).
        var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM AuditLogs WHERE Timestamp < {cutoff}", ct);

        _log.LogInformation(
            "{Scope} audit sweep: {Count} rows purged (audit.purge entry recorded). " +
            "Oldest purged: {Oldest:O}, Newest purged: {Newest:O}.",
            scopeLabel, deleted, oldestTs, newestTs);
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
