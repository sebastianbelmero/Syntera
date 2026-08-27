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
            GlobalExceptionLogger.LogDomainError(_log, ex.Code, ex.Message, ex);
            await WriteAsync(ctx, MapDomain(ex), ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            GlobalExceptionLogger.LogUnhandledError(_log, ctx.Request.Path, ex);
            var message = _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.";
            await WriteAsync(ctx, HttpStatusCode.InternalServerError, message, "UNHANDLED");
        }
    }

    private static HttpStatusCode MapDomain(DomainException ex)
    {
        if (ex is NotFoundException) return HttpStatusCode.NotFound;
        if (ex is BusinessRuleException) return HttpStatusCode.Conflict;
        if (ex is AuthenticationException) return HttpStatusCode.Unauthorized;
        if (ex is AuthorizationException) return HttpStatusCode.Forbidden;
        return HttpStatusCode.BadRequest;
    }

    private static async Task WriteAsync(
        HttpContext ctx, HttpStatusCode status, string message, string code)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse<object>.Fail(code, message);
        // CA1869: reuse a single cached JsonSerializerOptions instance —
        // creating one per request causes a reflection-driven cache miss
        // every time and is a measurable perf hit under load.
        var json = JsonSerializer.Serialize(payload, s_jsonOptions);
        await ctx.Response.WriteAsync(json);
    }

    // Allocated once at class load; CamelCase naming policy is thread-safe.
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>
/// Compile-time generated log delegates via the [LoggerMessage] source
/// generator. Eliminates CA1848/CA1873 (string interpolation / arg
/// boxing when logging is disabled) and gives every log call a stable
/// EventId for filtering in Seq/Datadog.
/// </summary>
internal static partial class GlobalExceptionLogger
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Domain error: {Code} {Message}")]
    public static partial void LogDomainError(ILogger logger, string code, string message, Exception ex);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Unhandled exception on {Path}")]
    public static partial void LogUnhandledError(ILogger logger, string path, Exception ex);
}
