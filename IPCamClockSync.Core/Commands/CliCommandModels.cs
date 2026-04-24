namespace IPCamClockSync.Core.Commands;

public enum CommandGroup
{
    Help,
    Operate,
    Service,
    Firewall,
    Config,
    Diagnose,
    Unknown,
}

public sealed class ParsedCommand
{
    public CommandGroup Group { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Action { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public bool IsValid { get; init; }

    public string? ErrorMessage { get; init; }
}
