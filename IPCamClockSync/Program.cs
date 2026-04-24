using IPCamClockSync.Cli;
using IPCamClockSync.ConsoleGui;

if (args.Length == 0)
{
    var app = new ConsoleGuiApp();
    app.Run();
    return 0;
}

return CliHost.Run(args);