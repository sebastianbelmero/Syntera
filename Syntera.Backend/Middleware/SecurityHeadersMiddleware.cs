using Microsoft.AspNetCore.Http;

namespace Syntera.Backend.Middleware;

/// <summary>
/// Adds security headers to every HTTP response. Specifically:
/// <list type="bullet">
///   <item><c>Content-Security-Policy</c> — strict CSP that blocks XSS by
///     disallowing inline scripts, eval, and third-party origins.</item>
///   <item><c>X-Content-Type-Options: nosniff</c> — prevents MIME-type sniffing.</item>
///   <item><c>X-Frame-Options: DENY</c> — prevents clickjacking via iframe embedding.</item>
///   <item><c>Referrer-Policy: strict-origin-when-cross-origin</c> — limits referrer leakage.</item>
///   <item><c>Permissions-Policy</c> — locks down camera/microphone/geolocation/etc to self only.</item>
/// </list>
///
/// SECURITY (H8): previously the app had no CSP header — a single XSS would
/// have allowed attackers to load external scripts, exfiltrate tokens, etc.
/// The CSP below is conservative; in Development it is relaxed to allow
/// Vite's HMR + React Refresh (which requires eval-style source maps).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _isDevelopment = env.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ── Content-Security-Policy ───────────────────────────────────
        // Production: strict — only self + a few necessary exceptions.
        // Development: relaxed for Vite HMR + React Refresh (inline eval
        // needed for fast refresh), plus ws://localhost:5173 for HMR socket.
        string csp = _isDevelopment
            ? "default-src 'self'; " +
              "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
              "style-src 'self' 'unsafe-inline'; " +
              "img-src 'self' data: blob: https:; " +
              "font-src 'self' data:; " +
              "connect-src 'self' ws://localhost:5173 ws://localhost:4173 http://localhost:5296 https://localhost:5296; " +
              "frame-ancestors 'none'; " +
              "base-uri 'self'; " +
              "form-action 'self'"
            : "default-src 'self'; " +
              "script-src 'self'; " +
              "style-src 'self' 'unsafe-inline'; " +  // CSS-in-JS libraries (e.g. styled-components) need inline styles
              "img-src 'self' data: blob:; " +
              "font-src 'self' data:; " +
              "connect-src 'self'; " +
              "frame-ancestors 'none'; " +
              "base-uri 'self'; " +
              "form-action 'self'; " +
              "object-src 'none'";

        // Don't override CSP that's already set (e.g., by reverse proxy).
        if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
            context.Response.Headers["Content-Security-Policy"] = csp;

        // ── X-Content-Type-Options: nosniff ───────────────────────────
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // ── X-Frame-Options: DENY (CSP frame-ancestors also covers this,
        // but X-Frame-Options is still respected by older browsers) ──
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // ── Referrer-Policy: strict-origin-when-cross-origin ──────────
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // ── Permissions-Policy: lock down powerful features ──────────
        // Syntax: feature=(allowlist). Empty allowlist = feature blocked.
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), interest-cohort=(), " +
            "browsing-topics=(), payment=(), usb=(), serial=(), bluetooth=()";

        // ── Strict-Transport-Security: handled by UseHsts() in Production,
        // not duplicated here to avoid double-header conflicts. ──

        await _next(context);
    }
}
