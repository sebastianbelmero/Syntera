using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Application.Services;

/// <summary>
/// Loads theme palettes from the Platform DB and caches them in-memory
/// for 5 minutes. Storing palettes in DB allows Platform Admin to update
/// brand colors without redeploying; the cache ensures no per-request
/// DB hit on the hot path (login → theme → response).
/// </summary>
public interface IThemeService
{
    Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default);
    Task InvalidateCacheAsync(Guid siteId);
}

public sealed class ThemeService : IThemeService
{
    private readonly PlatformDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ThemeService(PlatformDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<DTOs.Auth.ThemeDto> GetThemeAsync(Guid siteId, CancellationToken ct = default)
    {
        var cacheKey = $"theme:{siteId}";
        if (_cache.TryGetValue<DTOs.Auth.ThemeDto>(cacheKey, out var cached))
            return cached!;

        var theme = await _db.Themes.AsNoTracking().FirstOrDefaultAsync(t => t.SiteId == siteId, ct);
        DTOs.Auth.ThemeDto dto;
        if (theme is null)
        {
            dto = PlatformDefault();
        }
        else
        {
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

    private static DTOs.Auth.ThemePalette ParsePalette(string json, bool defaultPalette)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return defaultPalette ? DefaultDark() : DefaultLight();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new DTOs.Auth.ThemePalette(
                Primary:   GetProp(root, "primary",   defaultPalette ? "#60A5FA" : "#0B3D6F"),
                Accent:    GetProp(root, "accent",    defaultPalette ? "#22D3EE" : "#00A7B5"),
                Background:GetProp(root, "background",defaultPalette ? "#0F172A" : "#F8FAFC"),
                Surface:   GetProp(root, "surface",   defaultPalette ? "#1E293B" : "#FFFFFF"),
                Text:      GetProp(root, "text",      defaultPalette ? "#F1F5F9" : "#243447"),
                Muted:     GetProp(root, "muted",     defaultPalette ? "#94A3B8" : "#64748B"),
                Border:    GetProp(root, "border",    defaultPalette ? "#334155" : "#E2E8F0"),
                Success:   GetProp(root, "success",   "#10B981"),
                Warning:   GetProp(root, "warning",   "#F59E0B"),
                Danger:    GetProp(root, "danger",    "#EF4444"));
        }
        catch
        {
            return defaultPalette ? DefaultDark() : DefaultLight();
        }
    }

    private static string GetProp(JsonElement root, string name, string fallback)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? fallback
            : fallback;

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
