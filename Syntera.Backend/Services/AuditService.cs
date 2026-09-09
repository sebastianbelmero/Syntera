using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Services;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Data;

namespace Syntera.Backend.Services;

/// <summary>
/// Append-only, hash-chained audit log writer. Each entry's hash is
/// computed over (PreviousHash + canonical fields + BeforeJson + AfterJson).
/// The chain makes any retroactive tampering detectable: a verifier
/// recomputes the chain from row 1 and compares.
///
/// <para><b>COMPLIANCE (M3-fix):</b> Both <c>BeforeJson</c> AND <c>AfterJson</c>
/// are now included in the hash payload. Previously only <c>AfterJson</c>
/// was hashed — an attacker with DB write access could tamper with
/// <c>BeforeJson</c> (the "previous state" snapshot in an update event)
/// and the chain hash would still validate. This closes the integrity
/// gap for full ALCOA+ "Original" + "Consistent" compliance.</para>
///
/// <para><b>COMPLIANCE (Critical audit writes):</b> <see cref="LogCriticalAsync"/>
/// throws <see cref="AuditWriteException"/> on failure. Sensitive operations
/// (user.update, role_template.update, permission.grant, role_template.publish,
/// business_admin.assign, system_admin.assign) MUST use this method to
/// ensure the business operation is treated as failed if its audit trail
/// cannot be written. The regular <see cref="LogAsync"/> retains its
/// best-effort, never-throw behavior for non-critical events.</para>
///
/// <para>Retention: controlled by <c>Audit:RetentionYears</c> (default 10).
/// The <c>AuditRetentionService</c> archives old rows: it first writes an
/// <c>audit.purge</c> entry recording what is being purged (count, oldest
/// timestamp, newest timestamp, last hash before purge), then performs
/// the raw SQL DELETE. The chain remains verifiable because the
/// <c>audit.purge</c> entry's <c>PreviousHash</c> references the last
/// surviving entry's hash.</para>
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Best-effort audit log write. Returns <c>true</c> on success,
    /// <c>false</c> on failure (failure is logged but never thrown).
    /// Use this for non-critical events where the business operation
    /// should succeed even if the audit trail write fails.
    /// </summary>
    Task<bool> LogAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Critical audit log write — throws <see cref="AuditWriteException"/>
    /// on failure. Sensitive operations MUST use this to ensure that
    /// a missing audit trail is treated as a failed business operation
    /// (21 CFR Part 11 §11.10(e) "reliable audit trails").
    /// </summary>
    Task LogCriticalAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>Read audit logs. Filtered by site scope automatically based on current user.</summary>
    Task<IReadOnlyList<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public sealed record AuditEntry(
    Guid? SiteId,
    Guid? ActorUserId,
    string? ActorEmail,
    string? ActorIp,
    string? ActorUserAgent,
    string Action,
    string? TargetType,
    string? TargetId,
    string Outcome,
    string? ErrorMessage = null,
    string? BeforeJson = null,
    string? AfterJson = null,
    /// <summary>
    /// 21 CFR Part 11 §11.50 — signature manifestation. The meaning
    /// associated with the recorded action (e.g., "I approve this role
    /// template for publication", "I authorize this direct permission
    /// grant"). Default "action performed" for backward compatibility.
    /// </summary>
    string? SignatureMeaning = null);

public sealed record AuditLogDto(
    long Id,
    DateTime Timestamp,
    Guid? SiteId,
    Guid? ActorUserId,
    string? ActorEmail,
    string? ActorIp,
    string? ActorUserAgent,
    string Action,
    string? TargetType,
    string? TargetId,
    string Outcome,
    string? ErrorMessage,
    /// <summary>Previous state snapshot (for update events). NULL for create/disable events.</summary>
    string? BeforeJson,
    /// <summary>Post-state snapshot (or new entity representation). NULL for delete/disable events.</summary>
    string? AfterJson,
    /// <summary>21 CFR Part 11 §11.50 signature meaning.</summary>
    string? SignatureMeaning);

public sealed record AuditQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Action = null,
    Guid? ActorUserId = null,
    string? Outcome = null,
    int Skip = 0,
    int Take = 50);

/// <summary>
/// Thrown when a critical audit log write fails. Sensitive operations
/// that use <see cref="IAuditService.LogCriticalAsync"/> MUST propagate
/// this exception to the caller — the business operation is treated as
/// failed because its audit trail cannot be guaranteed (21 CFR Part 11
/// §11.10(e) compliance).
/// </summary>
public sealed class AuditWriteException : Exception
{
    public AuditWriteException(string action, Exception inner)
        : base($"Critical audit log write failed for action '{action}'. " +
               $"The business operation cannot be considered complete without a reliable " +
               $"audit trail (21 CFR Part 11 §11.10(e)). See inner exception for details.", inner)
    {
        Action = action;
    }

    public string Action { get; }
}

public sealed partial class AuditService : IAuditService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AuditService> _log;

    // FIX (P2 — hash-chain race): the previous read-PreviousHash → compute →
    // insert sequence was not atomic. Two concurrent requests auditing into the
    // same database could both read the SAME last hash, produce two rows that
    // claim the same predecessor, and silently FORK the chain — undermining the
    // tamper-evidence guarantee (a verifier can no longer reconstruct a single
    // linear history). Each append is now serialized per target database within
    // this process via a semaphore keyed by database identity.
    // NOTE: this protects a single app instance. For multi-instance deployments,
    // add a database-level lock (e.g. sp_getapplock on SQL Server) or a UNIQUE
    // index on PreviousHash + retry loop.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _chainLocks = new();

    private static SemaphoreSlim ChainLockFor(string databaseKey)
        => _chainLocks.GetOrAdd(databaseKey, _ => new SemaphoreSlim(1, 1));

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write audit log entry: {Action}")]
    private partial void LogAuditWriteFailure(Exception exception, string action);

    [LoggerMessage(Level = LogLevel.Critical, Message = "CRITICAL audit write failure for action {Action} — business operation will be rolled back. Inner: {InnerMessage}")]
    private partial void LogCriticalAuditWriteFailure(string action, string innerMessage);

    public AuditService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        ICurrentUserService current,
        ILogger<AuditService> log)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _current = current;
        _log = log;
    }

    public async Task<bool> LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await WriteAsync(entry, ct);
            return true;
        }
        catch (Exception ex)
        {
            // Audit log must NEVER cause a request to fail (for non-critical events).
            LogAuditWriteFailure(ex, entry.Action);
            return false;
        }
    }

    public async Task LogCriticalAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await WriteAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // Critical audit failure — alert and re-throw so the caller can
            // roll back the business operation. The caller MUST treat this
            // as a failed operation (return 5xx to the client).
            LogCriticalAuditWriteFailure(entry.Action, ex.Message);
            throw new AuditWriteException(entry.Action, ex);
        }
    }

    private async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        var log = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            SiteId = entry.SiteId,
            ActorUserId = entry.ActorUserId,
            ActorEmail = entry.ActorEmail,
            ActorIp = entry.ActorIp,
            ActorUserAgent = entry.ActorUserAgent,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Outcome = entry.Outcome,
            ErrorMessage = entry.ErrorMessage,
            BeforeJson = entry.BeforeJson,
            AfterJson = entry.AfterJson,
            SignatureMeaning = entry.SignatureMeaning,
        };

        // Write to the correct DB — each append is serialized per database to
        // keep the hash chain linear (see the _chainLocks comment above).
        if (entry.SiteId is null)
        {
            // Platform-level audit → master DB.
            var chainLock = ChainLockFor("platform");
            await chainLock.WaitAsync(ct);
            try
            {
                log.PreviousHash = await GetLastHashAsync(_platformDb.AuditLogs, ct);
                log.Hash = ComputeHash(log);
                _platformDb.AuditLogs.Add(log);
                await _platformDb.SaveChangesAsync(ct);
            }
            finally
            {
                chainLock.Release();
            }
        }
        else
        {
            // Site-level audit → site DB.
            // COMPLIANCE FIX (Sprint 1.5 follow-up): use ResolveForSiteAsync
            // with entry.SiteId.Value (provided by the caller) instead of
            // ResolveAsync() (which reads from JWT _current.SiteId). The
            // previous code broke Platform-Admin-initiated site-scoped
            // audits (e.g. ldap.write, theme.write, business_admin.assign)
            // because Platform Admin has no site_id in their JWT —
            // ResolveAsync() threw InvalidOperationException. This bug was
            // masked for years by LogAsync swallowing exceptions; Sprint
            // 1.5 changed sensitive callers to LogCriticalAsync (which
            // throws), exposing the latent bug. Use the explicit siteId
            // from the AuditEntry — the caller knows which site it's
            // auditing for.
            var siteDb = await _siteDbFactory.ResolveForSiteAsync(entry.SiteId.Value, ct);
            var siteChainLock = ChainLockFor($"site:{entry.SiteId.Value}");
            await siteChainLock.WaitAsync(ct);
            try
            {
                log.PreviousHash = await GetLastHashAsync(siteDb.AuditLogs, ct);
                log.Hash = ComputeHash(log);
                siteDb.AuditLogs.Add(log);
                await siteDb.SaveChangesAsync(ct);
            }
            finally
            {
                siteChainLock.Release();
            }
        }
    }

    public async Task<IReadOnlyList<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        // Platform admin → master DB.
        if (_current.IsPlatformAdmin)
        {
            var q = _platformDb.AuditLogs.AsNoTracking();
            if (query.From is not null) q = q.Where(x => x.Timestamp >= query.From);
            if (query.To is not null) q = q.Where(x => x.Timestamp <= query.To);
            if (!string.IsNullOrEmpty(query.Action)) q = q.Where(x => x.Action == query.Action);
            if (query.ActorUserId is not null) q = q.Where(x => x.ActorUserId == query.ActorUserId);
            if (!string.IsNullOrEmpty(query.Outcome)) q = q.Where(x => x.Outcome == query.Outcome);
            var rows = await q.OrderByDescending(x => x.Timestamp).Skip(query.Skip).Take(query.Take).ToListAsync(ct);
            return rows.Select(Map).ToList();
        }

        // Site user → site DB, scoped to own site.
        if (_current.SiteId is null)
            throw new UnauthorizedAccessException("Cannot query audit logs without site context.");

        var siteDb = await _siteDbFactory.ResolveAsync(ct);
        var sq = siteDb.AuditLogs.AsNoTracking();
        if (query.From is not null) sq = sq.Where(x => x.Timestamp >= query.From);
        if (query.To is not null) sq = sq.Where(x => x.Timestamp <= query.To);
        if (!string.IsNullOrEmpty(query.Action)) sq = sq.Where(x => x.Action == query.Action);
        if (query.ActorUserId is not null) sq = sq.Where(x => x.ActorUserId == query.ActorUserId);
        if (!string.IsNullOrEmpty(query.Outcome)) sq = sq.Where(x => x.Outcome == query.Outcome);
        var srows = await sq.OrderByDescending(x => x.Timestamp).Skip(query.Skip).Take(query.Take).ToListAsync(ct);
        return srows.Select(Map).ToList();
    }

    private static async Task<string> GetLastHashAsync(IQueryable<AuditLog> source, CancellationToken ct)
    {
        var last = await source.AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return last?.Hash ?? "";
    }

    private static string ComputeHash(AuditLog log)
    {
        // SECURITY (M3-fix): hash MUST include BOTH BeforeJson AND AfterJson.
        // - AfterJson: the "after" state snapshot of the affected entity.
        // - BeforeJson: the "previous" state snapshot (for update events).
        //
        // Without BeforeJson in the hash, an attacker with DB write access
        // could tamper with the "previous state" payload (e.g., hide what
        // fields were actually changed in a user.update) and the chain
        // hash would still validate. Including both closes this integrity
        // gap for full ALCOA+ "Original" + "Consistent" compliance.
        //
        // SignatureMeaning is also included to prevent tampering with the
        // 21 CFR Part 11 §11.50 signature manifestation record.
        var beforeJson = log.BeforeJson ?? "";
        var afterJson = log.AfterJson ?? "";
        var signatureMeaning = log.SignatureMeaning ?? "";
        var payload = $"{log.PreviousHash}|{log.Timestamp:O}|{log.SiteId}|{log.ActorUserId}|{log.ActorEmail}|{log.Action}|{log.TargetType}|{log.TargetId}|{log.Outcome}|{log.ErrorMessage}|{beforeJson}|{afterJson}|{signatureMeaning}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static AuditLogDto Map(AuditLog x) => new(
        Id: x.Id,
        Timestamp: x.Timestamp,
        SiteId: x.SiteId,
        ActorUserId: x.ActorUserId,
        ActorEmail: x.ActorEmail,
        ActorIp: x.ActorIp,
        ActorUserAgent: x.ActorUserAgent,
        Action: x.Action,
        TargetType: x.TargetType,
        TargetId: x.TargetId,
        Outcome: x.Outcome,
        ErrorMessage: x.ErrorMessage,
        BeforeJson: x.BeforeJson,
        AfterJson: x.AfterJson,
        SignatureMeaning: x.SignatureMeaning);
}
