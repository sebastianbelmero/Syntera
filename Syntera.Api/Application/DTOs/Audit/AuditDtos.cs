namespace Syntera.Application.DTOs.Audit;

public record AuditLogDto(
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

public record AuditLogQuery(
    DateTime? From,
    DateTime? To,
    string? Action,
    Guid? ActorUserId,
    Guid? TargetUserId,
    string? Outcome,
    int Skip = 0,
    int Take = 50);
