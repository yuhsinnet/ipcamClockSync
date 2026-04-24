using IPCamClockSync.Core.Runtime;

namespace IPCamClockSync.Core.Services;

public sealed class WindowsFirewallController
{
    public const string RuleName = "IPCamClockSync NTP UDP 123";

    private readonly ICommandRunner _runner;

    public WindowsFirewallController(ICommandRunner runner)
    {
        _runner = runner;
    }

    public CommandRunResult Enable() => _runner.Run(
        "netsh",
        $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport=123 profile=any remoteip=any");

    public CommandRunResult SetOpenMode()
    {
        _runner.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"");
        return _runner.Run(
            "netsh",
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport=123 profile=any remoteip=any");
    }

    public CommandRunResult SetStrictMode()
    {
        _runner.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"");
        return _runner.Run(
            "netsh",
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport=123 profile=domain,private remoteip=localsubnet");
    }

    public CommandRunResult Disable() => _runner.Run(
        "netsh",
        $"advfirewall firewall set rule name=\"{RuleName}\" new enable=no");

    public CommandRunResult Status() => _runner.Run(
        "netsh",
        $"advfirewall firewall show rule name=\"{RuleName}\"");

    public CommandRunResult Repair()
    {
        _runner.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"");
        return SetOpenMode();
    }
}
