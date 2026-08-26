namespace Syntera.Application.Common;

/// <summary>
/// Generic envelope returned by every API endpoint. The shape is
/// intentionally stable so the front-end has exactly one switchboard
/// for success, validation error, and domain error cases — no special
/// cases per endpoint.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public IEnumerable<FieldError> FieldErrors { get; init; } = [];

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    public static ApiResponse<T> Invalid(IEnumerable<FieldError> errors) =>
        new()
        {
            Success = false,
            ErrorCode = "VALIDATION_FAILED",
            Message = "One or more fields failed validation.",
            FieldErrors = errors,
        };
}

public sealed record FieldError(string Field, string Message);

/// <summary>
/// Generic paged result — used by all list endpoints that accept
/// <c>page</c>/<c>pageSize</c> query string params. The shape
/// matches the kalventis-ui AppGrid contract so the front-end can
/// bind directly without reshaping.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize is > 0
        ? (int)Math.Ceiling(Total / (double)PageSize)
        : 0;
}

public sealed record PageQuery(int Page = 1, int PageSize = 20, string? Search = null);
