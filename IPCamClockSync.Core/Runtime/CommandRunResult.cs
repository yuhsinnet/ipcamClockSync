namespace IPCamClockSync.Core.Runtime;

public sealed class CommandRunResult
{
    public int ExitCode { get; init; }

    public string Output { get; init; } = string.Empty;

    public bool Success => ExitCode == 0;
}
