namespace Syntera.Application.DTOs.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    string RefreshToken,
    UserProfile Profile);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserProfile(
    Guid Id,
    string Email,
    string? FullName,
    IReadOnlyList<string> Roles);
