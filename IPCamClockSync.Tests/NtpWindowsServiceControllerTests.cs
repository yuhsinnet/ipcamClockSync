using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.Tests;

public class NtpWindowsServiceControllerTests
{
    [Fact]
    public void Install_WhenCreateSucceeds_ShouldRunSetupCommandsInOrder()
    {
        var runner = new RecordingCommandRunner(
            new CommandRunResult { ExitCode = 0, Output = "create ok" },
            new CommandRunResult { ExitCode = 0, Output = "description ok" },
            new CommandRunResult { ExitCode = 0, Output = "failure ok" },
            new CommandRunResult { ExitCode = 0, Output = "failureflag ok" });

        var sut = new NtpWindowsServiceController(runner);
        var result = sut.Install("C:\\tools\\IPCamClockSync.NtpServer.exe");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, runner.Calls.Count);
        Assert.Equal("sc.exe", runner.Calls[0].FileName);
        Assert.Contains("create \"IPCamClockSync.NtpServer\"", runner.Calls[0].Arguments, StringComparison.Ordinal);
        Assert.Contains("start= delayed-auto", runner.Calls[0].Arguments, StringComparison.Ordinal);
        Assert.Contains("LocalService", runner.Calls[0].Arguments, StringComparison.Ordinal);
        Assert.Contains("description \"IPCamClockSync.NtpServer\"", runner.Calls[1].Arguments, StringComparison.Ordinal);
        Assert.Contains("failure \"IPCamClockSync.NtpServer\"", runner.Calls[2].Arguments, StringComparison.Ordinal);
        Assert.Contains("failureflag \"IPCamClockSync.NtpServer\" 1", runner.Calls[3].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_WhenCreateFailsWithAlreadyExists_ShouldContinue()
    {
        var runner = new RecordingCommandRunner(
            new CommandRunResult { ExitCode = 1, Output = "[SC] CreateService FAILED 1073: The specified service already exists." },
            new CommandRunResult { ExitCode = 0, Output = "description ok" },
            new CommandRunResult { ExitCode = 0, Output = "failure ok" },
            new CommandRunResult { ExitCode = 0, Output = "failureflag ok" });

        var sut = new NtpWindowsServiceController(runner);
        var result = sut.Install("C:\\tools\\IPCamClockSync.NtpServer.exe");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, runner.Calls.Count);
    }

    [Fact]
    public void Install_WhenCreateFailsForOtherReason_ShouldReturnCreateError()
    {
        var createError = new CommandRunResult { ExitCode = 5, Output = "access denied" };
        var runner = new RecordingCommandRunner(createError);

        var sut = new NtpWindowsServiceController(runner);
        var result = sut.Install("C:\\tools\\IPCamClockSync.NtpServer.exe");

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("access denied", result.Output);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void Restart_WhenStopReportsNotStarted_ShouldStillStartService()
    {
        var runner = new RecordingCommandRunner(
            new CommandRunResult { ExitCode = 1, Output = "SERVICE_NAME: IPCamClockSync.NtpServer service has not been started." },
            new CommandRunResult { ExitCode = 0, Output = "start ok" });

        var sut = new NtpWindowsServiceController(runner);
        var result = sut.Restart();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("stop \"IPCamClockSync.NtpServer\"", runner.Calls[0].Arguments, StringComparison.Ordinal);
        Assert.Contains("start \"IPCamClockSync.NtpServer\"", runner.Calls[1].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Restart_WhenStopFailsForOtherReason_ShouldReturnStopError()
    {
        var runner = new RecordingCommandRunner(
            new CommandRunResult { ExitCode = 2, Output = "stop failed" });

        var sut = new NtpWindowsServiceController(runner);
        var result = sut.Restart();

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("stop failed", result.Output);
        Assert.Single(runner.Calls);
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        private readonly Queue<CommandRunResult> _results;

        public RecordingCommandRunner(params CommandRunResult[] results)
        {
            _results = new Queue<CommandRunResult>(results);
        }

        public List<(string FileName, string Arguments)> Calls { get; } = new();

        public CommandRunResult Run(string fileName, string arguments)
        {
            Calls.Add((fileName, arguments));
            if (_results.Count == 0)
            {
                return new CommandRunResult { ExitCode = 0, Output = "ok" };
            }

            return _results.Dequeue();
        }
    }
}
