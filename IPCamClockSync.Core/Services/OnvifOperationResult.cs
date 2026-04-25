namespace IPCamClockSync.Core.Services;

public sealed class OnvifOperationResult
{
    public bool Success { get; init; }

    public string ErrorType { get; init; } = "none";

    public string Message { get; init; } = string.Empty;

    public static OnvifOperationResult Ok(string message)
    {
        return new OnvifOperationResult
        {
            Success = true,
            Message = message,
        };
    }

    public static OnvifOperationResult Fail(string errorType, string message)
    {
        return new OnvifOperationResult
        {
            Success = false,
            ErrorType = string.IsNullOrWhiteSpace(errorType) ? "unknown" : errorType,
            Message = message,
        };
    }
}
