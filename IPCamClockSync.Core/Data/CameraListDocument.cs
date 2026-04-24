using System.Text.Json.Serialization;

namespace IPCamClockSync.Core.Data;

public sealed class CameraListDocument
{
    public string Version { get; set; } = "1.0";

    public List<CameraRecord> Cameras { get; set; } = new();

    public static CameraListDocument CreateEmpty()
    {
        return new CameraListDocument();
    }

    public void Normalize()
    {
        Version = string.IsNullOrWhiteSpace(Version) ? "1.0" : Version.Trim();
        Cameras ??= new List<CameraRecord>();

        foreach (var camera in Cameras)
        {
            camera.Normalize();
        }
    }
}

public sealed class CameraRecord
{
    public string Id { get; set; } = string.Empty;

    public string Ip { get; set; } = string.Empty;

    public int Port { get; set; } = 80;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string PasswordEncrypted { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int ConnectionTimeoutSeconds { get; set; } = 15;

    [JsonIgnore]
    public string EndpointKey => $"{Ip}:{Port}";

    public void Normalize()
    {
        Id = Id?.Trim() ?? string.Empty;
        Ip = Ip?.Trim() ?? string.Empty;
        Username = Username?.Trim() ?? string.Empty;
        Password ??= string.Empty;
        PasswordEncrypted ??= string.Empty;
        Port = Port is <= 0 or > 65535 ? 80 : Port;
        ConnectionTimeoutSeconds = ConnectionTimeoutSeconds <= 0 ? 15 : ConnectionTimeoutSeconds;
    }
}
