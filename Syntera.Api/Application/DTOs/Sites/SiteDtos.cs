namespace Syntera.Application.DTOs.Sites;

public record SiteDto(
    Guid Id,
    string Code,
    string DisplayName,
    string DefaultThemeKey,
    bool IsEnabled,
    string? Notes,
    IReadOnlyList<string> LdapDomains,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SiteUpsertDto(
    string Code,
    string DisplayName,
    string DefaultThemeKey,
    string DatabaseConnectionString,
    string? Notes,
    List<string> LdapDomains);

public record LdapConfigDto(
    Guid SiteId,
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string EmailAttribute,
    string? BindDn,
    string UserFilterTemplate,
    int TimeoutSeconds,
    bool SearchSubtree,
    bool HasBindPassword);

public record LdapConfigUpsertDto(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string EmailAttribute,
    string? BindDn,
    string? BindPassword,
    string UserFilterTemplate,
    int TimeoutSeconds,
    bool SearchSubtree);

public record LdapTestRequest(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string EmailAttribute,
    string? BindDn,
    string? BindPassword,
    string UserFilterTemplate,
    int TimeoutSeconds,
    bool SearchSubtree,
    string TestEmail);

public record LdapTestResult(
    bool Success,
    string? Dn,
    string? DisplayName,
    string? Email,
    string? ErrorMessage,
    int LatencyMs);

public record ThemeDto(
    Guid SiteId,
    string ThemeKey,
    ThemePaletteDto Light,
    ThemePaletteDto Dark,
    string? LogoUrl);

public record ThemePaletteDto(
    string Primary,
    string Accent,
    string Background,
    string Surface,
    string Text,
    string Muted,
    string Border,
    string Success,
    string Warning,
    string Danger);

public record ThemeUpsertDto(
    string ThemeKey,
    ThemePaletteDto Light,
    ThemePaletteDto Dark,
    string? LogoUrl);
