using IPCamClockSync.Core.Commands;

namespace IPCamClockSync.Tests;

public class UnitTest1
{
    [Fact]
    public void Parse_ScanCommand_ShouldBeOperateGroup()
    {
        var parsed = CliCommandParser.Parse(new[] { "/scan" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Operate, parsed.Group);
        Assert.Equal("scan", parsed.Name);
    }

    [Fact]
    public void Parse_ServiceInstall_ShouldMapAction()
    {
        var parsed = CliCommandParser.Parse(new[] { "/ntpserver", "service", "install", "C:\\a.exe" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Service, parsed.Group);
        Assert.Equal("install", parsed.Action);
        Assert.Single(parsed.Arguments);
    }

    [Fact]
    public void Parse_FirewallRepair_ShouldMapFirewallGroup()
    {
        var parsed = CliCommandParser.Parse(new[] { "/ntpserver", "firewall", "repair" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Firewall, parsed.Group);
        Assert.Equal("repair", parsed.Action);
    }

    [Fact]
    public void Parse_FirewallModeOpen_ShouldMapFirewallModeAction()
    {
        var parsed = CliCommandParser.Parse(new[] { "/ntpserver", "firewall", "mode", "open" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Firewall, parsed.Group);
        Assert.Equal("mode-open", parsed.Action);
    }

    [Fact]
    public void Parse_UseNtp_ShouldMapOperateGroup()
    {
        var parsed = CliCommandParser.Parse(new[] { "/usentp", "192.168.0.1" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Operate, parsed.Group);
        Assert.Equal("use-ntp", parsed.Name);
        Assert.Equal("192.168.0.1", parsed.Arguments[0]);
    }

    [Fact]
    public void Parse_SetNtp_ShouldMapUseNtpAlias()
    {
        var parsed = CliCommandParser.Parse(new[] { "/set-ntp", "192.168.0.1" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Operate, parsed.Group);
        Assert.Equal("use-ntp", parsed.Name);
        Assert.Equal("192.168.0.1", parsed.Arguments[0]);
    }

    [Fact]
    public void Parse_NtpServerCliVerify_ShouldMapServiceGroup()
    {
        var parsed = CliCommandParser.Parse(new[] { "/ntpserver", "cli", "verify", "127.0.0.1", "3" });

        Assert.True(parsed.IsValid);
        Assert.Equal(CommandGroup.Service, parsed.Group);
        Assert.Equal("cli-verify", parsed.Action);
        Assert.Equal("127.0.0.1", parsed.Arguments[0]);
        Assert.Equal("3", parsed.Arguments[1]);
    }
}