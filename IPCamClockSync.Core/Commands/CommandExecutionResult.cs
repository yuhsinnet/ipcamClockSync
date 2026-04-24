namespace IPCamClockSync.Core.Commands;

public sealed class CommandExecutionResult
{
    public int ExitCode { get; init; }

    public string Message { get; init; } = string.Empty;
}
