using System.Globalization;

namespace IPCamClockSync.Core.Commands;

public static class CliCommandParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return Help();
        }

        var normalized = args.Select(a => a.Trim()).Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        if (normalized.Length == 0)
        {
            return Help();
        }

        var first = normalized[0].ToLowerInvariant();
        if (first is "/h" or "-h" or "--help" or "help")
        {
            return Help();
        }

        return first switch
        {
            "/scan" => Build(CommandGroup.Operate, "scan", normalized[1..]),
            "/a" => Build(CommandGroup.Operate, "update-once", normalized[1..]),
            "/set-ntp" => Build(CommandGroup.Operate, "set-ntp", normalized[1..]),
            "/validate" => Build(CommandGroup.Operate, "validate", normalized[1..]),
            "/export" => Build(CommandGroup.Operate, "export", normalized[1..]),
            "/ntpserver" => ParseNtpServer(normalized),
            _ => new ParsedCommand
            {
                Group = CommandGroup.Unknown,
                Name = first,
                Arguments = normalized[1..],
                IsValid = false,
                ErrorMessage = string.Format(CultureInfo.InvariantCulture, "Unknown command: {0}", first),
            },
        };
    }

    private static ParsedCommand ParseNtpServer(string[] args)
    {
        if (args.Length < 2)
        {
            return Invalid("/ntpserver requires a subcommand");
        }

        var second = args[1].ToLowerInvariant();
        if (second == "firewall")
        {
            if (args.Length < 3)
            {
                return Invalid("/ntpserver firewall requires action: status|enable|disable|repair|mode open|strict");
            }

            if (args[2].Equals("mode", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 4)
                {
                    return Invalid("/ntpserver firewall mode requires profile: open|strict");
                }

                var profile = args[3].ToLowerInvariant();
                if (profile is not ("open" or "strict"))
                {
                    return Invalid("/ntpserver firewall mode supports only: open|strict");
                }

                return Build(CommandGroup.Firewall, "firewall-mode", args[4..], $"mode-{profile}");
            }

            return Build(CommandGroup.Firewall, "firewall", args[3..], args[2].ToLowerInvariant());
        }

        if (second == "service")
        {
            if (args.Length < 3)
            {
                return Invalid("/ntpserver service requires action: install|uninstall|status");
            }

            return Build(CommandGroup.Service, "service", args[3..], args[2].ToLowerInvariant());
        }

        return second switch
        {
            "start" or "stop" or "restart" or "status" => Build(CommandGroup.Service, "service", args[2..], second),
            _ => Invalid($"Unsupported /ntpserver subcommand: {second}"),
        };
    }

    private static ParsedCommand Help() => new()
    {
        Group = CommandGroup.Help,
        Name = "help",
        IsValid = true,
    };

    private static ParsedCommand Build(CommandGroup group, string name, IReadOnlyList<string> arguments, string? action = null) => new()
    {
        Group = group,
        Name = name,
        Action = action,
        Arguments = arguments,
        IsValid = true,
    };

    private static ParsedCommand Invalid(string message) => new()
    {
        Group = CommandGroup.Unknown,
        Name = "invalid",
        IsValid = false,
        ErrorMessage = message,
    };
}
