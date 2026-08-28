namespace Syntera.Application.DTOs.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    UserProfileDto Profile,
    ThemeDto Theme);

public record RefreshRequest(string RefreshToken);

public record RefreshResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    UserProfileDto Profile,
    ThemeDto Theme);

public record UserProfileDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Scope,        // "platform" | "site"
    Guid? SiteId,
    string? SiteCode,
    string? SiteDisplayName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public record ThemeDto(
    string ThemeKey,
    ThemePalette Light,
    ThemePalette Dark,
    string? LogoUrl);

public record ThemePalette(
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

public record LogoutRequest(string RefreshToken);
