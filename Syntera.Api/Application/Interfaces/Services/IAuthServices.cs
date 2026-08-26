namespace Syntera.Application.Interfaces.Services;

/// <summary>
/// Token issuance abstraction — kept interface-only so the auth
/// controller depends on a contract, not on JwtSecurityTokenHandler
/// directly. Useful for tests and for swapping in an external
/// IdentityProvider (Keycloak / Auth0) without touching the controller.
/// </summary>
public interface ITokenService
{
    (string AccessToken, DateTime ExpiresAt, string? RefreshToken) IssueFor(
        string userId,
        string userName,
        string email,
        IEnumerable<string> roles);

    string? ValidateRefreshToken(string refreshToken);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsInRole(string role);
}
