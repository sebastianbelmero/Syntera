using Microsoft.EntityFrameworkCore;
using Syntera.Application.DTOs.Sites;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Ldap;

namespace Syntera.Application.Services;

public interface ISiteManagementService
{
    Task<IReadOnlyList<SiteDto>> ListAsync(CancellationToken ct = default);
    Task<SiteDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Update editable fields (DisplayName, LdapDomains). Code &amp; ConnectionString are locked.</summary>
    Task<SiteDto> UpdateAsync(Guid id, SiteUpdateDto dto, Guid updatedBy, CancellationToken ct = default);

    Task<LdapConfigDto> GetLdapConfigAsync(Guid siteId, CancellationToken ct = default);
    Task<LdapConfigDto> UpsertLdapConfigAsync(Guid siteId, LdapConfigUpsertDto dto, Guid updatedBy, CancellationToken ct = default);
    Task<LdapTestResult> TestLdapAsync(LdapTestRequest req, CancellationToken ct = default);

    Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default);
    Task<DTOs.Auth.ThemeDto> UpsertThemeAsync(Guid siteId, ThemeUpsertDto dto, Guid updatedBy, CancellationToken ct = default);
}

public sealed class SiteManagementService : ISiteManagementService
{
    private readonly PlatformDbContext _db;
    private readonly ILdapClient _ldap;
    private readonly IThemeService _themes;
    private readonly IAuditService _audit;

    public SiteManagementService(
        PlatformDbContext db,
        ILdapClient ldap,
        IThemeService themes,
        IAuditService audit)
    {
        _db = db;
        _ldap = ldap;
        _themes = themes;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SiteDto>> ListAsync(CancellationToken ct = default)
    {
        var sites = await _db.Sites.AsNoTracking()
            .Include(s => s.LdapDomains)
            .OrderBy(s => s.Code)
            .ToListAsync(ct);
        return sites.Select(MapSite).ToList();
    }

    public async Task<SiteDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var site = await _db.Sites.AsNoTracking()
            .Include(s => s.LdapDomains)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Site", id);
        return MapSite(site);
    }

    /// <summary>
    /// Updates the editable fields of a site: DisplayName and LdapDomains.
    /// Code, DatabaseConnectionString, and IsEnabled are NOT editable from
    /// the frontend — they are managed via backend configuration.
    /// </summary>
    public async Task<SiteDto> UpdateAsync(Guid id, SiteUpdateDto dto, Guid updatedBy, CancellationToken ct = default)
    {
        var site = await _db.Sites
            .Include(s => s.LdapDomains)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Site", id);

        site.DisplayName = dto.DisplayName;

        // Diff domains — allow add/remove.
        var newDomains = dto.LdapDomains.Select(d => d.ToLowerInvariant()).Distinct().ToList();
        var existing = site.LdapDomains.ToList();

        // Validate uniqueness across all sites.
        foreach (var d in newDomains)
        {
            if (await _db.LdapDomains.AnyAsync(x => x.Domain == d && x.SiteId != id, ct))
                throw new BusinessRuleException("DOMAIN_TAKEN", $"Email domain '{d}' is already mapped to another site.");
        }

        var toRemove = existing.Where(e => !newDomains.Contains(e.Domain)).ToList();
        var toAdd = newDomains.Where(d => !existing.Any(e => e.Domain == d)).ToList();
        foreach (var r in toRemove) site.LdapDomains.Remove(r);
        foreach (var a in toAdd) site.LdapDomains.Add(new SiteLdapDomain { Domain = a, IsActive = true });

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: updatedBy, ActorEmail: null, ActorIp: null, ActorUserAgent: null,
            Action: "site.update", TargetType: "Site", TargetId: site.Id.ToString(),
            Outcome: "success", ErrorMessage: null,
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { site.DisplayName, Domains = newDomains })), ct);

        return MapSite(site);
    }

    public async Task<LdapConfigDto> GetLdapConfigAsync(Guid siteId, CancellationToken ct = default)
    {
        var cfg = await _db.LdapConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.SiteId == siteId, ct);
        if (cfg is null)
        {
            // Return defaults if no config yet.
            return new LdapConfigDto(siteId, Host: "", Port: 389, UseStartTls: false, BaseDn: "", UpnDomain: null);
        }
        return MapLdapConfig(cfg);
    }

    public async Task<LdapConfigDto> UpsertLdapConfigAsync(Guid siteId, LdapConfigUpsertDto dto, Guid updatedBy, CancellationToken ct = default)
    {
        // Validate port.
        if (dto.Port <= 0 || dto.Port > 65535)
            throw new BusinessRuleException("INVALID_PORT", "Port must be between 1 and 65535.");

        // Note: Plain LDAP on port 389 is allowed. The operator accepts the
        // risk of cleartext password transmission for internal networks.

        var cfg = await _db.LdapConfigs.FirstOrDefaultAsync(c => c.SiteId == siteId, ct);
        var isNew = cfg is null;
        cfg ??= new SiteLdapConfig { SiteId = siteId };

        cfg.Host = dto.Host;
        cfg.Port = dto.Port;
        cfg.UseStartTls = dto.UseStartTls;
        cfg.BaseDn = dto.BaseDn;
        cfg.UpnDomain = string.IsNullOrWhiteSpace(dto.UpnDomain) ? null : dto.UpnDomain;

        if (isNew) _db.LdapConfigs.Add(cfg);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: siteId, ActorUserId: updatedBy, ActorEmail: null, ActorIp: null, ActorUserAgent: null,
            Action: "ldap.write", TargetType: "SiteLdapConfig", TargetId: siteId.ToString(),
            Outcome: "success", ErrorMessage: null,
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { cfg.Host, cfg.Port, cfg.UseStartTls, cfg.BaseDn, cfg.UpnDomain })), ct);

        return MapLdapConfig(cfg);
    }

    public async Task<LdapTestResult> TestLdapAsync(LdapTestRequest req, CancellationToken ct = default)
    {
        var endpoint = new LdapEndpoint(
            Host: req.Host, Port: req.Port, UseStartTls: req.UseStartTls,
            BaseDn: req.BaseDn, UpnDomain: req.UpnDomain);

        // Direct bind: test with the user's actual email + password.
        var result = await _ldap.TestConnectionAsync(endpoint, req.TestEmail, req.TestPassword, ct);

        return new LdapTestResult(
            Success: result.IsSuccess,
            Dn: result.Dn,
            DisplayName: result.DisplayName,
            Email: result.Email,
            ErrorMessage: result.ErrorMessage,
            LatencyMs: result.LatencyMs);
    }

    public async Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default)
        => await _themes.GetThemeAsync(siteId, ct);

    public async Task<DTOs.Auth.ThemeDto> UpsertThemeAsync(Guid siteId, ThemeUpsertDto dto, Guid updatedBy, CancellationToken ct = default)
    {
        var theme = await _db.Themes.FirstOrDefaultAsync(t => t.SiteId == siteId, ct);
        var isNew = theme is null;
        theme ??= new SiteTheme { SiteId = siteId };

        theme.ThemeKey = dto.ThemeKey;
        theme.LightPaletteJson = System.Text.Json.JsonSerializer.Serialize(dto.Light);
        theme.DarkPaletteJson = System.Text.Json.JsonSerializer.Serialize(dto.Dark);
        theme.LogoUrl = dto.LogoUrl;

        if (isNew) _db.Themes.Add(theme);
        await _db.SaveChangesAsync(ct);

        await _themes.InvalidateCacheAsync(siteId);

        await _audit.LogAsync(new AuditEntry(
            SiteId: siteId, ActorUserId: updatedBy, ActorEmail: null, ActorIp: null, ActorUserAgent: null,
            Action: "theme.write", TargetType: "SiteTheme", TargetId: siteId.ToString(),
            Outcome: "success", ErrorMessage: null,
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { dto.ThemeKey })), ct);

        return await _themes.GetThemeAsync(siteId, ct);
    }

    private static SiteDto MapSite(Site s) => new(
        Id: s.Id, Code: s.Code, DisplayName: s.DisplayName,
        DefaultThemeKey: s.DefaultThemeKey, IsEnabled: s.IsEnabled, Notes: s.Notes,
        LdapDomains: s.LdapDomains.Select(d => d.Domain).ToList(),
        CreatedAt: s.CreatedAt, UpdatedAt: s.UpdatedAt);

    private static LdapConfigDto MapLdapConfig(SiteLdapConfig c) => new(
        SiteId: c.SiteId, Host: c.Host, Port: c.Port, UseStartTls: c.UseStartTls,
        BaseDn: c.BaseDn, UpnDomain: c.UpnDomain);
}
