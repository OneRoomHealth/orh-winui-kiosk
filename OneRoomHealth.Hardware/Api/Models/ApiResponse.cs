namespace OneRoomHealth.Hardware.Api.Models;

/// <summary>
/// Standard API response wrapper for success responses.
/// </summary>
/// <typeparam name="T">The type of data being returned.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// The response data.
    /// </summary>
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
}

/// <summary>
/// API response for error cases.
/// </summary>
public class ApiErrorResponse
{
    /// <summary>
    /// Always false for error responses.
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Error information.
    /// </summary>
    public ErrorInfo? Error { get; set; }

    public static ApiErrorResponse FromException(Exception ex, string? code = null)
    {
        return new ApiErrorResponse
        {
            Error = new ErrorInfo
            {
                Code = code ?? "INTERNAL_ERROR",
                Message = ex.Message,
                Details = ex.StackTrace
            }
        };
    }

    public static ApiErrorResponse FromMessage(string code, string message, string? details = null)
    {
        return new ApiErrorResponse
        {
            Error = new ErrorInfo
            {
                Code = code,
                Message = message,
                Details = details
            }
        };
    }
}

/// <summary>
/// Error information structure.
/// </summary>
public class ErrorInfo
{
    /// <summary>
    /// Error code (e.g., "DEVICE_NOT_FOUND", "INVALID_REQUEST").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional additional error details.
    /// </summary>
    public string? Details { get; init; }
}
