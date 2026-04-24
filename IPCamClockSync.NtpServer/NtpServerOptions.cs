namespace IPCamClockSync.NtpServer;

public sealed class NtpServerOptions
{
    public int Port { get; init; } = 123;

    public string BindAddress { get; init; } = "0.0.0.0";
}
