using IPCamClockSync.Core.Commands;
using IPCamClockSync.Core.Logging;
using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.Cli;

public static class CliHost
{
    public static int Run(string[] args)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var parserResult = CliCommandParser.Parse(args);

        var commandRunner = new ProcessCommandRunner();
        var serviceController = new NtpWindowsServiceController(commandRunner);
        var firewallController = new WindowsFirewallController(commandRunner);
        var dispatcher = new CliCommandDispatcher(serviceController, firewallController);

        var result = dispatcher.Execute(parserResult);

        var logger = new JsonlLogger(Path.Combine(AppContext.BaseDirectory, "logs"));
        logger.Write(result.ExitCode == 0 ? "INFO" : "ERROR", "cli", result.Message, correlationId);

        Console.WriteLine(result.Message);
        return result.ExitCode;
    }
}