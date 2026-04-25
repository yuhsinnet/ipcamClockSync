using IPCamClockSync.Core.Data;

namespace IPCamClockSync.Core.Discovery;

public static class DiscoveredCameraMapper
{
    public static CameraListDocument ToCameraList(IEnumerable<DiscoveredCamera> cameras, int timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(cameras);

        var document = new CameraListDocument();
        var index = 1;

        foreach (var camera in cameras)
        {
            var uri = TryCreateUri(camera.ServiceAddress);
            document.Cameras.Add(new CameraRecord
            {
                Id = $"cam-{index:000}",
                Ip = uri?.Host ?? camera.Address,
                Port = uri?.Port > 0 ? uri.Port : 80,
                Model = camera.Model,
                Username = string.Empty,
                Password = string.Empty,
                Enabled = true,
                ConnectionTimeoutSeconds = timeoutSeconds,
            });
            index++;
        }

        document.Normalize();
        return document;
    }

    private static Uri? TryCreateUri(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) ? uri : null;
    }
}
