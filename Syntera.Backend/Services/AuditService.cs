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
/// computed over (PreviousHash + canonical JSON of the entry). The chain
/// makes any retroactive tampering detectable: a verifier recomputes the
/// chain from row 1 and compares.
///
/// Retention: controlled by Platform setting <c>Audit:RetentionYears</c>
/// (default 10). A monthly background job archives rows older than the
/// retention window to cold storage and prunes them from the hot table.
/// Archived rows are kept indefinitely in cold storage for compliance.
/// </summary>
public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);

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
    string? AfterJson = null);

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
    string? ErrorMessage);

public sealed record AuditQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Action = null,
    Guid? ActorUserId = null,
    string? Outcome = null,
    int Skip = 0,
    int Take = 50);

public sealed partial class AuditService : IAuditService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AuditService> _log;

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write audit log entry: {Action}")]
    private partial void LogAuditWriteFailure(Exception exception, string action);

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

    public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
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
            };

            // Write to the correct DB.
            if (entry.SiteId is null)
            {
                // Platform-level audit → master DB.
                log.PreviousHash = await GetLastHashAsync(_platformDb.AuditLogs, ct);
                log.Hash = ComputeHash(log);
                _platformDb.AuditLogs.Add(log);
                await _platformDb.SaveChangesAsync(ct);
            }
            else
            {
                // Site-level audit → site DB.
                var siteDb = await _siteDbFactory.ResolveAsync(ct);
                log.PreviousHash = await GetLastHashAsync(siteDb.AuditLogs, ct);
                log.Hash = ComputeHash(log);
                siteDb.AuditLogs.Add(log);
                await siteDb.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Audit log must NEVER cause a request to fail.
            LogAuditWriteFailure(ex, entry.Action);
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
        // SECURITY (M3): hash MUST include AfterJson (the "after" state snapshot
        // of the affected entity). Without it, an attacker with DB write access
        // could tamper with the AfterJson payload (e.g., hide what fields were
        // actually changed in a user.update) and the chain hash would still
        // validate. Including AfterJson closes this integrity gap.
        var afterJson = log.AfterJson ?? "";
        var payload = $"{log.PreviousHash}|{log.Timestamp:O}|{log.SiteId}|{log.ActorUserId}|{log.ActorEmail}|{log.Action}|{log.TargetType}|{log.TargetId}|{log.Outcome}|{log.ErrorMessage}|{afterJson}";
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
        ErrorMessage: x.ErrorMessage);
}
