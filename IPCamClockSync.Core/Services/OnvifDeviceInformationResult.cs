namespace IPCamClockSync.Core.Services;

public sealed class OnvifDeviceInformationResult
{
    public bool Success { get; init; }

    public string ErrorCategory { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    public string FirmwareVersion { get; init; } = string.Empty;

    public static OnvifDeviceInformationResult Ok(string model, string manufacturer, string firmwareVersion)
    {
        return new OnvifDeviceInformationResult
        {
            Success = true,
            Model = model?.Trim() ?? string.Empty,
            Manufacturer = manufacturer?.Trim() ?? string.Empty,
            FirmwareVersion = firmwareVersion?.Trim() ?? string.Empty,
            Message = "Device information fetched.",
        };
    }

    public static OnvifDeviceInformationResult Fail(string errorCategory, string message)
    {
        return new OnvifDeviceInformationResult
        {
            Success = false,
            ErrorCategory = errorCategory?.Trim() ?? string.Empty,
            Message = message?.Trim() ?? string.Empty,
        };
    }
}