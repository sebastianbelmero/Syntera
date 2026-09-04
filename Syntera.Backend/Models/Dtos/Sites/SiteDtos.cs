namespace Syntera.Backend.Models.Dtos.Sites;

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

/// <summary>
/// Editable fields for a site. <c>Code</c> is locked (used in JWT claim and config).
/// <c>DatabaseConnectionString</c> is locked (managed via backend config).
/// Only <c>DisplayName</c> and <c>LdapDomains</c> are editable from the frontend.
/// </summary>
public record SiteUpdateDto(
    string DisplayName,
    List<string> LdapDomains);

public record LdapConfigDto(
    Guid SiteId,
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string? UpnDomain);

public record LdapConfigUpsertDto(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string? UpnDomain);

public record LdapTestRequest(
    string Host,
    int Port,
    bool UseStartTls,
    string BaseDn,
    string? UpnDomain,
    string TestEmail,
    string TestPassword);

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
