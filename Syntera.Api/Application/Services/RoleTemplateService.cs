using Microsoft.EntityFrameworkCore;
using Syntera.Application.DTOs.Roles;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;
using Syntera.Infrastructure.Data;

namespace Syntera.Application.Services;

/// <summary>
/// Manages role templates (defined by Platform Admin). When a template is
/// published, it is cloned into every enabled site's database as a Role
/// with corresponding RolePermissions. Existing roles with the same key
/// are updated in-place (their permission set is replaced, version bumped).
/// </summary>
public interface IRoleTemplateService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(RoleTemplateUpsertDto dto, Guid createdBy, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, RoleTemplateUpsertDto dto, CancellationToken ct = default);
    Task PublishAsync(Guid id, Guid publishedBy, CancellationToken ct = default);

    Task<PermissionCatalogDto> GetPermissionCatalogAsync(CancellationToken ct = default);
}

public sealed partial class RoleTemplateService : IRoleTemplateService
{
    private readonly PlatformDbContext _db;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly IAuditService _audit;
    private readonly ILogger<RoleTemplateService> _log;

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to clone role template {TemplateKey} to site {SiteCode}")]
    private partial void LogCloneFailure(Exception exception, string templateKey, string siteCode);

    public RoleTemplateService(
        PlatformDbContext db,
        ISiteDbContextFactory siteDbFactory,
        IAuditService audit,
        ILogger<RoleTemplateService> log)
    {
        _db = db;
        _siteDbFactory = siteDbFactory;
        _audit = audit;
        _log = log;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var templates = await _db.RoleTemplates.AsNoTracking()
            .Include(t => t.Permissions)
            .OrderBy(t => t.Key)
            .ToListAsync(ct);
        return templates.Select(Map).ToList();
    }

    public async Task<RoleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.RoleTemplates.AsNoTracking()
            .Include(x => x.Permissions)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("RoleTemplate", id);
        return Map(t);
    }

    public async Task<RoleDto> CreateAsync(RoleTemplateUpsertDto dto, Guid createdBy, CancellationToken ct = default)
    {
        if (await _db.RoleTemplates.AnyAsync(t => t.Key == dto.Key, ct))
            throw new BusinessRuleException("KEY_TAKEN", $"Role template key '{dto.Key}' is already in use.");

        var template = new RoleTemplate
        {
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            IsSiteAdminRole = dto.IsSiteAdminRole,
            IsPublished = false,
            Version = 1,
        };

        foreach (var k in dto.PermissionKeys.Distinct())
            template.Permissions.Add(new RoleTemplatePermission { PermissionKey = k });

        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: createdBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.create", TargetType: "RoleTemplate", TargetId: template.Id.ToString(),
            Outcome: "success"), ct);

        return Map(template);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, RoleTemplateUpsertDto dto, CancellationToken ct = default)
    {
        // ─── 100% raw SQL, zero EF tracking ───────────────────────────
        // Previous EF-based approaches threw DbUpdateConcurrencyException
        // due to change-tracker state conflicts. Raw SQL with a transaction
        // is bulletproof and atomic.
        using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // 1. UPDATE the role template row.
            var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE RoleTemplates
                SET [Key] = {dto.Key},
                    DisplayName = {dto.DisplayName},
                    Description = {dto.Description ?? (string?)null},
                    IsSiteAdminRole = {dto.IsSiteAdminRole},
                    UpdatedAt = {DateTime.UtcNow}
                WHERE Id = {id}", ct);

            if (updated == 0)
                throw new NotFoundException("RoleTemplate", id);

            // 2. DELETE all existing permission rows.
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM RoleTemplatePermissions
                WHERE RoleTemplateId = {id}", ct);

            // 3. INSERT new permission rows.
            var now = DateTime.UtcNow;
            foreach (var k in dto.PermissionKeys.Distinct())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO RoleTemplatePermissions (Id, RoleTemplateId, PermissionKey, CreatedAt, UpdatedAt)
                    VALUES ({Guid.NewGuid()}, {id}, {k}, {now}, {now})", ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Reload for the response DTO (read-only, no tracking).
        var result = await _db.RoleTemplates
            .AsNoTracking()
            .Include(x => x.Permissions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return Map(result!);
    }

    public async Task PublishAsync(Guid id, Guid publishedBy, CancellationToken ct = default)
    {
        var template = await _db.RoleTemplates
            .Include(t => t.Permissions)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("RoleTemplate", id);

        template.IsPublished = true;
        template.Version++;
        await _db.SaveChangesAsync(ct);

        // Clone into every enabled site.
        var sites = await _db.Sites.Where(s => s.IsEnabled).ToListAsync(ct);
        foreach (var site in sites)
        {
            try
            {
                await CloneTemplateToSiteAsync(template, site, ct);
            }
            catch (Exception ex)
            {
                LogCloneFailure(ex, template.Key, site.Code);
            }
        }

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: publishedBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.publish", TargetType: "RoleTemplate", TargetId: template.Id.ToString(),
            Outcome: "success",
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { template.Key, template.Version, SitesClonedTo = sites.Count })), ct);
    }

    private async Task CloneTemplateToSiteAsync(RoleTemplate template, Site site, CancellationToken ct)
    {
        var siteDb = await _siteDbFactory.ResolveAsync(ct);
        var role = await siteDb.Roles.FirstOrDefaultAsync(r => r.Key == template.Key, ct);

        if (role is null)
        {
            role = new Role
            {
                Key = template.Key,
                DisplayName = template.DisplayName,
                Description = template.Description,
                IsSiteAdminRole = template.IsSiteAdminRole,
                OriginTemplateId = template.Id,
            };
            siteDb.Roles.Add(role);
        }
        else
        {
            role.DisplayName = template.DisplayName;
            role.Description = template.Description;
            role.IsSiteAdminRole = template.IsSiteAdminRole;
            role.OriginTemplateId = template.Id;
        }

        // Sync permissions: load all permission rows matching template keys.
        var desiredKeys = template.Permissions.Select(p => p.PermissionKey).ToList();
        var desiredPerms = await siteDb.Permissions
            .Where(p => desiredKeys.Contains(p.Key))
            .ToListAsync(ct);

        // Remove existing role-permission rows.
        var existingRps = await siteDb.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync(ct);
        siteDb.RolePermissions.RemoveRange(existingRps);

        foreach (var p in desiredPerms)
        {
            siteDb.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        }

        // Bump all users with this role so they get fresh perm resolution.
        var affectedUserIds = await siteDb.UserRoles
            .Where(ur => ur.RoleId == role.Id)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var uid in affectedUserIds)
        {
            var u = await siteDb.Users.FirstOrDefaultAsync(x => x.Id == uid, ct);
            if (u is not null) u.PermissionsVersion++;
        }

        await siteDb.SaveChangesAsync(ct);
    }

    public async Task<PermissionCatalogDto> GetPermissionCatalogAsync(CancellationToken ct = default)
    {
        var catalog = PermissionCatalog.Static;
        var groups = catalog.Groups.Select(g => new PermissionGroupDto(
            g.Group,
            g.Permissions.Select(p => new PermissionDto(
                Id: Guid.Empty, // catalog is static, no DB id
                Key: p.Key,
                DisplayName: p.Description,
                Group: g.Group,
                IsPlatformOnly: false)).ToList())).ToList();
        await Task.CompletedTask;
        return new PermissionCatalogDto(groups);
    }

    private static RoleDto Map(RoleTemplate t) => new(
        Id: t.Id, Key: t.Key, DisplayName: t.DisplayName, Description: t.Description,
        IsSiteAdminRole: t.IsSiteAdminRole, IsPublished: t.IsPublished, Version: t.Version,
        PermissionKeys: t.Permissions.Select(p => p.PermissionKey).ToList(),
        CreatedAt: t.CreatedAt, UpdatedAt: t.UpdatedAt);
}
