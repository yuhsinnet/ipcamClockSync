using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.Core.Commands;

public sealed class CliCommandDispatcher
{
    private readonly NtpWindowsServiceController _service;
    private readonly WindowsFirewallController _firewall;

    public CliCommandDispatcher(NtpWindowsServiceController service, WindowsFirewallController firewall)
    {
        _service = service;
        _firewall = firewall;
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!command.IsValid)
        {
            return new CommandExecutionResult { ExitCode = 2, Message = command.ErrorMessage ?? "Invalid command." };
        }

        return command.Group switch
        {
            CommandGroup.Help => new CommandExecutionResult { ExitCode = 0, Message = HelpText() },
            CommandGroup.Operate => ExecuteOperate(command),
            CommandGroup.Service => ExecuteService(command),
            CommandGroup.Firewall => ExecuteFirewall(command),
            _ => new CommandExecutionResult { ExitCode = 2, Message = "Unsupported command group." },
        };
    }

    private static CommandExecutionResult ExecuteOperate(ParsedCommand command)
    {
        var msg = command.Name switch
        {
            "scan" => "Scan placeholder: ONVIF scan will be implemented in next milestone.",
            "update-once" => "Update placeholder: single-time sync pipeline reserved.",
            "set-ntp" => "Set-NTP placeholder: ONVIF NTP push reserved.",
            "validate" => "Validate placeholder: camera list validation reserved.",
            "export" => "Export placeholder: settings and camera list export reserved.",
            _ => "Unknown operate command.",
        };

        var code = msg.StartsWith("Unknown", StringComparison.Ordinal) ? 2 : 0;
        return new CommandExecutionResult { ExitCode = code, Message = msg };
    }

    private CommandExecutionResult ExecuteService(ParsedCommand command)
    {
        var runResult = (command.Action ?? string.Empty) switch
        {
            "install" => _service.Install(command.Arguments.FirstOrDefault() ?? "IPCamClockSync.NtpServer.exe"),
            "uninstall" => _service.Uninstall(),
            "start" => _service.Start(),
            "stop" => _service.Stop(),
            "restart" => _service.Restart(),
            "status" => _service.Status(),
            _ => new CommandRunResult { ExitCode = 2, Output = "Unsupported service action." },
        };

        return new CommandExecutionResult
        {
            ExitCode = runResult.ExitCode,
            Message = runResult.Output,
        };
    }

    private CommandExecutionResult ExecuteFirewall(ParsedCommand command)
    {
        var runResult = (command.Action ?? string.Empty) switch
        {
            "enable" => _firewall.Enable(),
            "disable" => _firewall.Disable(),
            "status" => _firewall.Status(),
            "repair" => _firewall.Repair(),
            _ => new CommandRunResult { ExitCode = 2, Output = "Unsupported firewall action." },
        };

        return new CommandExecutionResult
        {
            ExitCode = runResult.ExitCode,
            Message = runResult.Output,
        };
    }

    private static string HelpText() =>
@"IPCamClockSync CLI

operate:
  /scan
  /a
  /set-ntp <ntp-ip>
  /validate
  /export

service:
  /ntpserver service install <path-to-exe>
  /ntpserver service uninstall
  /ntpserver start|stop|restart|status

firewall:
  /ntpserver firewall status|enable|disable|repair

help:
  /h";
}
