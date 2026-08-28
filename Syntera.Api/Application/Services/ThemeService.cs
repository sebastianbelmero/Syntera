using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Application.Services;

public interface IThemeService
{
    Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default);
    Task InvalidateCacheAsync(Guid siteId);
}

public sealed class ThemeService : IThemeService
{
    private readonly PlatformDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ThemeService>? _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ThemeService(PlatformDbContext db, IMemoryCache cache, ILogger<ThemeService>? logger = null)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default)
    {
        var cacheKey = $"theme:{siteId}";
        if (_cache.TryGetValue<DTOs.Auth.ThemeDto>(cacheKey, out var cached))
        {
            _logger?.LogDebug("Theme cache hit for site {SiteId}: themeKey={ThemeKey}", siteId, cached?.ThemeKey);
            return cached!;
        }

        var theme = await _db.Themes.AsNoTracking().FirstOrDefaultAsync(t => t.SiteId == siteId, ct);
        DTOs.Auth.ThemeDto dto;

        if (theme is null)
        {
            _logger?.LogWarning("No SiteTheme record found for site {SiteId} — returning PlatformDefault", siteId);
            dto = PlatformDefault();
        }
        else
        {
            _logger?.LogInformation("Loading theme for site {SiteId}: themeKey={ThemeKey}, lightJson={LightJson}",
                siteId, theme.ThemeKey, theme.LightPaletteJson?.Substring(0, Math.Min(80, theme.LightPaletteJson?.Length ?? 0)));

            var light = ParsePalette(theme.LightPaletteJson, defaultPalette: false);
            var dark = ParsePalette(theme.DarkPaletteJson, defaultPalette: true);
            dto = new DTOs.Auth.ThemeDto(
                ThemeKey: theme.ThemeKey,
                Light: light,
                Dark: dark,
                LogoUrl: theme.LogoUrl);
        }

        _cache.Set(cacheKey, dto, CacheTtl);
        return dto;
    }

    public Task InvalidateCacheAsync(Guid siteId)
    {
        _cache.Remove($"theme:{siteId}");
        return Task.CompletedTask;
    }

    public static DTOs.Auth.ThemeDto PlatformDefault() => new(
        ThemeKey: "syntera-default",
        Light: DefaultLight(),
        Dark: DefaultDark(),
        LogoUrl: null);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static DTOs.Auth.ThemePalette ParsePalette(string? json, bool defaultPalette)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return defaultPalette ? DefaultDark() : DefaultLight();

        try
        {
            // Deserialize with case-insensitive property matching — handles
            // both PascalCase (from DbSeeder) and camelCase (from frontend).
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
            if (parsed is null)
                return defaultPalette ? DefaultDark() : DefaultLight();

            string Get(string key, string fallback) =>
                parsed.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val) ? val : fallback;

            return new DTOs.Auth.ThemePalette(
                Primary:   Get("Primary",   defaultPalette ? "#60A5FA" : "#0B3D6F"),
                Accent:    Get("Accent",    defaultPalette ? "#22D3EE" : "#00A7B5"),
                Background:Get("Background",defaultPalette ? "#0F172A" : "#F8FAFC"),
                Surface:   Get("Surface",   defaultPalette ? "#1E293B" : "#FFFFFF"),
                Text:      Get("Text",      defaultPalette ? "#F1F5F9" : "#243447"),
                Muted:     Get("Muted",     defaultPalette ? "#94A3B8" : "#64748B"),
                Border:    Get("Border",    defaultPalette ? "#334155" : "#E2E8F0"),
                Success:   Get("Success",   "#10B981"),
                Warning:   Get("Warning",   "#F59E0B"),
                Danger:    Get("Danger",    "#EF4444"));
        }
        catch
        {
            return defaultPalette ? DefaultDark() : DefaultLight();
        }
    }

    public static DTOs.Auth.ThemePalette DefaultLight() => new(
        Primary: "#0B3D6F", Accent: "#00A7B5",
        Background: "#F8FAFC", Surface: "#FFFFFF",
        Text: "#243447", Muted: "#64748B",
        Border: "#E2E8F0",
        Success: "#10B981", Warning: "#F59E0B", Danger: "#EF4444");

    public static DTOs.Auth.ThemePalette DefaultDark() => new(
        Primary: "#60A5FA", Accent: "#22D3EE",
        Background: "#0F172A", Surface: "#1E293B",
        Text: "#F1F5F9", Muted: "#94A3B8",
        Border: "#334155",
        Success: "#34D399", Warning: "#FBBF24", Danger: "#F87171");
}
