namespace Syntera.Application.DTOs.Common;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Skip,
    int Take);

public record ApiErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public record ApiSuccessResponse<T>(T Data, string? Message = null);

public record SimpleResult(bool Success, string? Message = null);
