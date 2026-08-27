using Microsoft.EntityFrameworkCore;
using Syntera.Application.DTOs.Sites;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Ldap;
using Syntera.Infrastructure.Security;

namespace Syntera.Application.Services;

public interface ISiteManagementService
{
    Task<IReadOnlyList<SiteDto>> ListAsync(CancellationToken ct = default);
    Task<SiteDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<SiteDto> CreateAsync(SiteUpsertDto dto, Guid createdBy, CancellationToken ct = default);
    Task<SiteDto> UpdateAsync(Guid id, SiteUpsertDto dto, CancellationToken ct = default);
    Task DisableAsync(Guid id, CancellationToken ct = default);

    Task<LdapConfigDto> GetLdapConfigAsync(Guid siteId, CancellationToken ct = default);
    Task<LdapConfigDto> UpsertLdapConfigAsync(Guid siteId, LdapConfigUpsertDto dto, CancellationToken ct = default);
    Task<LdapTestResult> TestLdapAsync(LdapTestRequest req, CancellationToken ct = default);

    Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default);
    Task<DTOs.Auth.ThemeDto> UpsertThemeAsync(Guid siteId, ThemeUpsertDto dto, CancellationToken ct = default);
}

public sealed class SiteManagementService : ISiteManagementService
{
    private readonly PlatformDbContext _db;
    private readonly ILdapConfigProtector _protector;
    private readonly ILdapClient _ldap;
    private readonly IThemeService _themes;
    private readonly IAuditService _audit;

    public SiteManagementService(
        PlatformDbContext db,
        ILdapConfigProtector protector,
        ILdapClient ldap,
        IThemeService themes,
        IAuditService audit)
    {
        _db = db;
        _protector = protector;
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

    public async Task<SiteDto> CreateAsync(SiteUpsertDto dto, Guid createdBy, CancellationToken ct = default)
    {
        // Validate code uniqueness.
        if (await _db.Sites.AnyAsync(s => s.Code == dto.Code, ct))
            throw new BusinessRuleException("SITE_CODE_TAKEN", $"Site code '{dto.Code}' is already in use.");

        // Validate domain uniqueness.
        foreach (var d in dto.LdapDomains)
        {
            if (await _db.LdapDomains.AnyAsync(x => x.Domain == d, ct))
                throw new BusinessRuleException("DOMAIN_TAKEN", $"Email domain '{d}' is already mapped to another site.");
        }

        var site = new Site
        {
            Code = dto.Code,
            DisplayName = dto.DisplayName,
            DatabaseConnectionString = dto.DatabaseConnectionString,
            DefaultThemeKey = dto.DefaultThemeKey,
            Notes = dto.Notes,
            IsEnabled = true,
        };

        foreach (var d in dto.LdapDomains)
        {
            site.LdapDomains.Add(new SiteLdapDomain { Domain = d.ToLowerInvariant(), IsActive = true });
        }

        _db.Sites.Add(site);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: createdBy, ActorEmail: null, ActorIp: null, ActorUserAgent: null,
            Action: "site.create", TargetType: "Site", TargetId: site.Id.ToString(),
            Outcome: "success", ErrorMessage: null,
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { site.Code, site.DisplayName })), ct);

        return MapSite(site);
    }

    public async Task<SiteDto> UpdateAsync(Guid id, SiteUpsertDto dto, CancellationToken ct = default)
    {
        var site = await _db.Sites
            .Include(s => s.LdapDomains)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Site", id);

        if (site.Code != dto.Code && await _db.Sites.AnyAsync(s => s.Code == dto.Code && s.Id != id, ct))
            throw new BusinessRuleException("SITE_CODE_TAKEN", $"Site code '{dto.Code}' is already in use.");

        site.Code = dto.Code;
        site.DisplayName = dto.DisplayName;
        site.DatabaseConnectionString = dto.DatabaseConnectionString;
        site.DefaultThemeKey = dto.DefaultThemeKey;
        site.Notes = dto.Notes;

        // Diff domains.
        var existing = site.LdapDomains.ToList();
        var toRemove = existing.Where(e => !dto.LdapDomains.Contains(e.Domain)).ToList();
        var toAdd = dto.LdapDomains.Where(d => !existing.Any(e => e.Domain == d)).ToList();
        foreach (var r in toRemove) site.LdapDomains.Remove(r);
        foreach (var a in toAdd) site.LdapDomains.Add(new SiteLdapDomain { Domain = a.ToLowerInvariant(), IsActive = true });

        await _db.SaveChangesAsync(ct);
        return MapSite(site);
    }

    public async Task DisableAsync(Guid id, CancellationToken ct = default)
    {
        var site = await _db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Site", id);
        site.IsEnabled = false;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<LdapConfigDto> GetLdapConfigAsync(Guid siteId, CancellationToken ct = default)
    {
        var cfg = await _db.LdapConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.SiteId == siteId, ct)
            ?? throw new NotFoundException("LdapConfig", siteId);
        return MapLdapConfig(cfg);
    }

    public async Task<LdapConfigDto> UpsertLdapConfigAsync(Guid siteId, LdapConfigUpsertDto dto, CancellationToken ct = default)
    {
        // Enforce TLS: never accept plain LDAP.
        if (dto.Port == 389 && !dto.UseStartTls)
            throw new BusinessRuleException("PLAIN_LDAP_FORBIDDEN",
                "Plain LDAP on port 389 without StartTLS is forbidden. Use port 636 (LDAPS) or enable StartTLS.");

        if (dto.Port != 389 && dto.Port != 636)
            throw new BusinessRuleException("INVALID_PORT", "Port must be 389 (with StartTLS) or 636 (LDAPS).");

        var cfg = await _db.LdapConfigs.FirstOrDefaultAsync(c => c.SiteId == siteId, ct);
        var isNew = cfg is null;
        cfg ??= new SiteLdapConfig { SiteId = siteId };

        cfg.Host = dto.Host;
        cfg.Port = dto.Port;
        cfg.UseStartTls = dto.UseStartTls;
        cfg.BaseDn = dto.BaseDn;
        cfg.EmailAttribute = dto.EmailAttribute;
        cfg.BindDn = dto.BindDn;
        cfg.UserFilterTemplate = dto.UserFilterTemplate;
        cfg.TimeoutSeconds = dto.TimeoutSeconds;
        cfg.SearchSubtree = dto.SearchSubtree;

        if (!string.IsNullOrEmpty(dto.BindPassword))
        {
            cfg.BindPasswordEncrypted = _protector.Protect(dto.BindPassword);
        }

        if (isNew) _db.LdapConfigs.Add(cfg);
        await _db.SaveChangesAsync(ct);

        return MapLdapConfig(cfg);
    }

    public async Task<LdapTestResult> TestLdapAsync(LdapTestRequest req, CancellationToken ct = default)
    {
        if (req.Port == 389 && !req.UseStartTls)
            throw new BusinessRuleException("PLAIN_LDAP_FORBIDDEN",
                "Plain LDAP test is forbidden. Enable StartTLS.");

        var endpoint = new LdapEndpoint(
            Host: req.Host, Port: req.Port, UseStartTls: req.UseStartTls,
            BaseDn: req.BaseDn, EmailAttribute: req.EmailAttribute,
            UserFilterTemplate: req.UserFilterTemplate,
            TimeoutSeconds: req.TimeoutSeconds, SearchSubtree: req.SearchSubtree);

        var creds = new LdapCredentials(req.BindDn, req.BindPassword);

        // Use AuthenticateAsync with a deliberately wrong password to test the lookup path
        // without actually logging anyone in. If the failure message indicates "Invalid
        // credentials" we know the connection, bind, and search all worked.
        var result = await _ldap.AuthenticateAsync(endpoint, creds, req.TestEmail, "TEST_INVALID_PASSWORD_xyz", ct);

        if (result.IsSuccess)
        {
            // Should not happen with a wrong password, but if it does, treat as success.
            return new LdapTestResult(true, result.Dn, result.DisplayName, result.Email, null, result.LatencyMs);
        }

        // Differentiate "invalid credentials" (means LDAP worked) from real errors.
        if (result.ErrorMessage != null &&
            (result.ErrorMessage.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase)
             || result.ErrorMessage.Contains("Invalid Credentials", StringComparison.OrdinalIgnoreCase)
             || result.ErrorMessage.Contains("LDAP bind failed", StringComparison.OrdinalIgnoreCase)))
        {
            return new LdapTestResult(true, result.Dn, result.DisplayName, result.Email,
                "Connection OK. User found. (Password deliberately wrong for test.)", result.LatencyMs);
        }

        return new LdapTestResult(false, null, null, null, result.ErrorMessage, result.LatencyMs);
    }

    public async Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default)
        => await _themes.GetThemeAsync(siteId, ct);

    public async Task<DTOs.Auth.ThemeDto> UpsertThemeAsync(Guid siteId, ThemeUpsertDto dto, CancellationToken ct = default)
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
        return await _themes.GetThemeAsync(siteId, ct);
    }

    private static SiteDto MapSite(Site s) => new(
        Id: s.Id, Code: s.Code, DisplayName: s.DisplayName,
        DefaultThemeKey: s.DefaultThemeKey, IsEnabled: s.IsEnabled, Notes: s.Notes,
        LdapDomains: s.LdapDomains.Select(d => d.Domain).ToList(),
        CreatedAt: s.CreatedAt, UpdatedAt: s.UpdatedAt);

    private static LdapConfigDto MapLdapConfig(SiteLdapConfig c) => new(
        SiteId: c.SiteId, Host: c.Host, Port: c.Port, UseStartTls: c.UseStartTls,
        BaseDn: c.BaseDn, EmailAttribute: c.EmailAttribute, BindDn: c.BindDn,
        UserFilterTemplate: c.UserFilterTemplate, TimeoutSeconds: c.TimeoutSeconds,
        SearchSubtree: c.SearchSubtree, HasBindPassword: !string.IsNullOrEmpty(c.BindPasswordEncrypted));
}
