using IPCamClockSync.Core.Runtime;

namespace IPCamClockSync.Core.Services;

public sealed class NtpWindowsServiceController
{
    public const string DefaultServiceName = "IPCamClockSync.NtpServer";

    private readonly ICommandRunner _runner;

    public NtpWindowsServiceController(ICommandRunner runner)
    {
        _runner = runner;
    }

    public CommandRunResult Install(string exePath)
    {
        var create = _runner.Run(
            "sc.exe",
            $"create \"{DefaultServiceName}\" binPath= \"\\\"{exePath}\\\"\" start= delayed-auto obj= \"NT AUTHORITY\\LocalService\" DisplayName= \"IPCamClockSync NTP Server\"");

        if (!create.Success && !create.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return create;
        }

        var desc = _runner.Run("sc.exe", $"description \"{DefaultServiceName}\" \"NTP server for IPCamClockSync.\"");
        if (!desc.Success)
        {
            return desc;
        }

        var failure = _runner.Run("sc.exe", $"failure \"{DefaultServiceName}\" reset= 0 actions= restart/5000/restart/10000/restart/30000");
        if (!failure.Success)
        {
            return failure;
        }

        return _runner.Run("sc.exe", $"failureflag \"{DefaultServiceName}\" 1");
    }

    public CommandRunResult Uninstall() => _runner.Run("sc.exe", $"delete \"{DefaultServiceName}\"");

    public CommandRunResult Start() => _runner.Run("sc.exe", $"start \"{DefaultServiceName}\"");

    public CommandRunResult Stop() => _runner.Run("sc.exe", $"stop \"{DefaultServiceName}\"");

    public CommandRunResult Restart()
    {
        var stop = Stop();
        if (!stop.Success && !stop.Output.Contains("service has not been started", StringComparison.OrdinalIgnoreCase))
        {
            return stop;
        }

        return Start();
    }

    public CommandRunResult Status() => _runner.Run("sc.exe", $"query \"{DefaultServiceName}\"");
}
