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
}