using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Syntera.Application.Interfaces.Services;

namespace Syntera.Infrastructure.Identity;

/// <summary>
/// Reads the authenticated user from the current <see cref="HttpContext"/>
/// claims. Keyed per-request (scoped) so each request gets its own
/// instance populated by JWT middleware.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    public Guid? UserId
    {
        get
        {
            var id = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var g) ? g : null;
        }
    }

    public string? UserName => _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public IReadOnlyCollection<string> Roles
        => _http.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsInRole(string role)
        => _http.HttpContext?.User?.IsInRole(role) ?? false;
}
