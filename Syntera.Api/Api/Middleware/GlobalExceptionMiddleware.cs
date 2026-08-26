using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Syntera.Application.Common;
using Syntera.Domain.Exceptions;

namespace Syntera.Api.Middleware;

/// <summary>
/// Single-point exception-to-HTTP mapper. Every unhandled exception
/// flows through here, so controllers stay free of try/catch noise
/// and the front-end always sees a uniform error envelope.
///
/// Mapping table:
///   NotFoundException     → 404
///   BusinessRuleException→ 409
///   ValidationException  → 400 (handled by [ApiController] pipeline)
///   Everything else      → 500 (logged; never leak internal text in prod)
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _log;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> log,
        IHostEnvironment env)
    {
        _next = next;
        _log = log;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (DomainException ex)
        {
            _log.LogWarning(ex, "Domain error: {Code} {Message}", ex.Code, ex.Message);
            await WriteAsync(ctx, MapDomain(ex), ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
            var message = _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.";
            await WriteAsync(ctx, HttpStatusCode.InternalServerError, message, "UNHANDLED");
        }
    }

    private static HttpStatusCode MapDomain(DomainException ex)
    {
        if (ex is NotFoundException) return HttpStatusCode.NotFound;
        if (ex is BusinessRuleException) return HttpStatusCode.Conflict;
        return HttpStatusCode.BadRequest;
    }

    private static async Task WriteAsync(
        HttpContext ctx, HttpStatusCode status, string message, string code)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse<object>.Fail(code, message);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await ctx.Response.WriteAsync(json);
    }
}
