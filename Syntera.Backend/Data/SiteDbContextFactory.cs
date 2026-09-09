using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Syntera.Backend.Services;
using Syntera.Backend.Data;

namespace Syntera.Backend.Data;

/// <summary>
/// Resolves the correct <see cref="SiteDbContext"/> for the current request,
/// based on the <c>site_id</c> claim in the JWT. Platform Admin requests
/// (no site_id claim) never resolve a SiteDbContext — attempting to do so
/// throws, which fails-closed for tenant safety.
///
/// <para>The connection string is fetched from the Platform DB's <see cref="Site"/>
/// table on first access per request and cached for the request lifetime.
/// COMPLIANCE (Sprint 2.6): the stored value may be encrypted via DPAPI
/// (prefixed with <c>"ENC:"</c>); <see cref="IConnectionStringProtector.Unprotect"/>
/// is called before passing the value to <c>UseSqlServer</c>. Plaintext
/// values (no <c>ENC:</c> prefix) are returned as-is by the protector —
/// backward compat with existing DBs that have not yet been migrated.</para>
/// </summary>
public interface ISiteDbContextFactory
{
    /// <summary>
    /// Returns the SiteDbContext for the current request's site (from JWT claim).
    /// Throws if the request has no site_id claim (i.e., Platform Admin).
    /// </summary>
    Task<SiteDbContext> ResolveAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the SiteDbContext for an EXPLICIT siteId. Used by Platform Admin
    /// endpoints that need to manage data in a specific site (e.g., bootstrap
    /// the first site-business-admin). Throws if the site is not found or disabled.
    /// </summary>
    Task<SiteDbContext> ResolveForSiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>
    /// FIX (P0 — multi-site scan bug): returns a FRESH, UNCACHED SiteDbContext for
    /// the given site. The caller owns disposal.
    ///
    /// <para><b>Why this exists:</b> <see cref="ResolveForSiteAsync"/> caches the
    /// first resolved context per request and IGNORES the siteId argument on
    /// subsequent calls — correct for normal requests (one request = one site)
    /// but WRONG for flows that must iterate multiple sites in a single request.
    /// The refresh fallback in AuthService.RefreshAsync scans every enabled
    /// site's RefreshTokens table; with the cached resolver, every iteration
    /// after the first queried the FIRST site's database, so site users from
    /// any other site could never find their token → REFRESH_NOT_FOUND on every
    /// page reload. This method always resolves the requested site's connection
    /// string and returns a new context.</para>
    /// </summary>
    Task<SiteDbContext> CreateForSiteAsync(Guid siteId, CancellationToken ct = default);
}

/// <summary>
/// Scoped factory. Caches the resolved SiteDbContext for the lifetime of
/// the HTTP request — calling ResolveAsync multiple times within the same
/// request returns the same instance.
/// </summary>
public sealed class SiteDbContextFactory : ISiteDbContextFactory, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ICurrentUserService _currentUser;
    private readonly IConnectionStringProtector _protector;
    private SiteDbContext? _resolved;
    private bool _disposed;

    public SiteDbContextFactory(
        IServiceProvider services,
        ICurrentUserService currentUser,
        IConnectionStringProtector protector)
    {
        _services = services;
        _currentUser = currentUser;
        _protector = protector;
    }

    public async Task<SiteDbContext> ResolveAsync(CancellationToken ct = default)
    {
        if (_resolved is not null) return _resolved;

        var siteId = _currentUser.SiteId
            ?? throw new InvalidOperationException(
                "Cannot resolve SiteDbContext: current request has no site_id claim. " +
                "Platform Admin requests must use ResolveForSiteAsync(siteId) instead.");

        return await ResolveForSiteAsync(siteId, ct);
    }

    public async Task<SiteDbContext> ResolveForSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        if (_resolved is not null) return _resolved;

        // Fetch connection string from Platform DB.
        using var scope = _services.CreateScope();
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var site = await platformDb.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new InvalidOperationException($"Site {siteId} not found in platform registry.");

        if (!site.IsEnabled)
            throw new InvalidOperationException($"Site '{site.Code}' is disabled.");

        // COMPLIANCE (Sprint 2.6): decrypt the connection string before
        // handing it to SiteDbContext. Handles both ENC:-prefixed
        // (encrypted) and bare (plaintext) values — the protector returns
        // plaintext values verbatim, so existing DBs that have not yet
        // been migrated by the seeder continue to resolve correctly.
        // Throws InvalidOperationException if the key ring is missing or
        // the ciphertext is corrupted — surfaces as a 500 to the Platform
        // Admin (fail closed — we never fall back to a partial string).
        var connectionString = _protector.Unprotect(site.DatabaseConnectionString);

        // Build a fresh SiteDbContext using the resolved connection string.
        var options = new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Site"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        _resolved = new SiteDbContext(options);
        return _resolved;
    }

    /// <summary>
    /// FIX (P0 — multi-site scan bug): always creates a FRESH context for the
    /// requested site (no per-request caching). Caller must dispose. See the
    /// interface documentation for the multi-site scan rationale.
    /// </summary>
    public async Task<SiteDbContext> CreateForSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        var connectionString = await ResolveConnectionStringAsync(siteId, ct);
        return new SiteDbContext(BuildSiteOptions(connectionString));
    }

    /// <summary>
    /// Fetch + decrypt the site's connection string from the Platform DB registry.
    /// Shared by ResolveForSiteAsync (cached per request) and CreateForSiteAsync
    /// (fresh per call).
    /// </summary>
    private async Task<string> ResolveConnectionStringAsync(Guid siteId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var site = await platformDb.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new InvalidOperationException($"Site {siteId} not found in platform registry.");

        if (!site.IsEnabled)
            throw new InvalidOperationException($"Site '{site.Code}' is disabled.");

        return _protector.Unprotect(site.DatabaseConnectionString);
    }

    private static DbContextOptions<SiteDbContext> BuildSiteOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Site"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _resolved?.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        if (_resolved is not null) return _resolved.DisposeAsync();
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
