using System.Diagnostics;
using System.Text;

namespace IPCamClockSync.Core.Runtime;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandRunResult Run(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new CommandRunResult { ExitCode = 1, Output = "Failed to start process." };
        }

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(process.StandardOutput.ReadToEnd());
        outputBuilder.Append(process.StandardError.ReadToEnd());

        process.WaitForExit();
        return new CommandRunResult
        {
            ExitCode = process.ExitCode,
            Output = outputBuilder.ToString().Trim(),
        };
    }
}
