using IPCamClockSync.Core.Data;

namespace IPCamClockSync.Core.Services;

public interface IOnvifDeviceManagementService
{
    Task<OnvifOperationResult> SetSystemDateAndTimeAsync(
        CameraRecord camera,
        DateTimeOffset localNow,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<OnvifOperationResult> SetNtpServerAsync(
        CameraRecord camera,
        string ntpIp,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<OnvifOperationResult> SetTimeToNtpModeAsync(
        CameraRecord camera,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
