using Microsoft.AspNetCore.Mvc;
using Syntera.Application.Common;

namespace Syntera.Api.Controllers;

/// <summary>
/// Base controller providing shared concerns — uniform
/// <see cref="ApiResponse{T}"/> wrapping, validation short-circuit,
/// and access to the current user id (when authenticated).
/// Keeps every controller DRY: no per-controller boilerplate for
/// error envelopes or user context extraction.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Ok<T>(T data, string? message = null)
        => base.Ok(ApiResponse<T>.Ok(data, message));

    /// <summary>
    /// Returns raw data without the <see cref="ApiResponse{T}"/> envelope.
    /// Used exclusively for DevExtreme grid endpoints — the DevExtreme
    /// JS client expects a flat <c>{ data, totalCount }</c> shape and
    /// cannot unwrap a custom envelope. All other endpoints stay
    /// envelope-wrapped for client-side uniformity.
    /// </summary>
    protected IActionResult OkRaw<T>(T data)
        => base.Ok(data);

    protected IActionResult Fail(string code, string message, int statusCode = 400)
        => StatusCode(statusCode, ApiResponse<object>.Fail(code, message));

    protected IActionResult Invalid(IEnumerable<FieldError> errors)
        => BadRequest(ApiResponse<object>.Invalid(errors));
}
