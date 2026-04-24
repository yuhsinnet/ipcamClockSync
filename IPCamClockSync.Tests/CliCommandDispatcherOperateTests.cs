using IPCamClockSync.Core.Commands;
using IPCamClockSync.Core.Configuration;
using IPCamClockSync.Core.Data;
using IPCamClockSync.Core.Discovery;
using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.Tests;

public sealed class CliCommandDispatcherOperateTests
{
    [Fact]
    public void Execute_SetNtp_ShouldPersistTargetIpToEnabledCameras()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcam-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var paths = new ApplicationDataPaths
        {
            ConfigDirectory = root,
            SettingsFilePath = Path.Combine(root, "settings.yaml"),
            CamerasFilePath = Path.Combine(root, "cameras.json"),
            GuiSettingsFilePath = Path.Combine(root, "consolegui.settings.json"),
            ExportDirectory = Path.Combine(root, "export"),
        };

        var settingsStore = new YamlSettingsStore();
        var cameraStore = new CameraListStore();

        settingsStore.Save(paths.SettingsFilePath, AppSettings.CreateDefault());
        cameraStore.Save(paths.CamerasFilePath, new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "cam-1", Ip = "192.168.1.10", Enabled = true, Username = "admin", Password = "p1" },
                new CameraRecord { Id = "cam-2", Ip = "192.168.1.11", Enabled = false, Username = "admin", Password = "p2" },
            },
        });

        var dispatcher = BuildDispatcher(paths, settingsStore, cameraStore);

        var result = dispatcher.Execute(CliCommandParser.Parse(new[] { "/set-ntp", "10.0.0.5" }));

        var loaded = cameraStore.Load(paths.CamerasFilePath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("10.0.0.5", loaded.Cameras.Single(c => c.Id == "cam-1").NtpServerIp);
        Assert.Equal(string.Empty, loaded.Cameras.Single(c => c.Id == "cam-2").NtpServerIp);
    }

    [Fact]
    public void Execute_UpdateOnce_ShouldReturnAuthErrorWhenCredentialMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcam-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var paths = new ApplicationDataPaths
        {
            ConfigDirectory = root,
            SettingsFilePath = Path.Combine(root, "settings.yaml"),
            CamerasFilePath = Path.Combine(root, "cameras.json"),
            GuiSettingsFilePath = Path.Combine(root, "consolegui.settings.json"),
            ExportDirectory = Path.Combine(root, "export"),
        };

        var settings = AppSettings.CreateDefault();
        settings.TimeUpdate.RetryCount = 0;
        settings.TimeUpdate.RequestTimeoutSeconds = 1;

        var settingsStore = new YamlSettingsStore();
        var cameraStore = new CameraListStore();

        settingsStore.Save(paths.SettingsFilePath, settings);
        cameraStore.Save(paths.CamerasFilePath, new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "cam-auth", Ip = "192.168.1.12", Enabled = true, Username = string.Empty, Password = string.Empty },
            },
        });

        var dispatcher = BuildDispatcher(paths, settingsStore, cameraStore);

        var result = dispatcher.Execute(CliCommandParser.Parse(new[] { "/a" }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error=auth", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CliCommandDispatcher BuildDispatcher(
        ApplicationDataPaths paths,
        YamlSettingsStore settingsStore,
        CameraListStore cameraStore)
    {
        var runner = new FakeCommandRunner();
        return new CliCommandDispatcher(
            paths,
            settingsStore,
            cameraStore,
            new StubOnvifDiscoveryService(),
            new NtpWindowsServiceController(runner),
            new WindowsFirewallController(runner));
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public CommandRunResult Run(string fileName, string arguments)
        {
            return new CommandRunResult { ExitCode = 0, Output = "ok" };
        }
    }
}
