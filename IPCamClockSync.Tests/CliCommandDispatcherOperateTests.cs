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
    public void Execute_UseNtp_ShouldPersistTargetIpToEnabledCameras()
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

        var result = dispatcher.Execute(CliCommandParser.Parse(new[] { "/usentp", "10.0.0.5" }));

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

    [Fact]
    public void Execute_UpdateOnce_ShouldRetryNetworkThenSucceed()
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
        settings.TimeUpdate.RetryCount = 1;
        settings.TimeUpdate.MaxConcurrency = 1;

        var settingsStore = new YamlSettingsStore();
        var cameraStore = new CameraListStore();
        settingsStore.Save(paths.SettingsFilePath, settings);
        cameraStore.Save(paths.CamerasFilePath, new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "cam-retry", Ip = "192.168.1.30", Enabled = true, Username = "admin", Password = "1234" },
            },
        });

        var attempts = 0;
        var fakeService = new FakeOnvifDeviceManagementService
        {
            TimeUpdateHandler = (camera, _) =>
            {
                attempts++;
                return attempts == 1
                    ? OnvifOperationResult.Fail("network", "temporary network error")
                    : OnvifOperationResult.Ok("time updated");
            },
        };

        var dispatcher = BuildDispatcher(paths, settingsStore, cameraStore, fakeService);

        var result = dispatcher.Execute(CliCommandParser.Parse(new[] { "/a" }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("attempts=2", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[OK]", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_UseNtp_ShouldReturnDeviceAuthFailureAndKeepLocalValue()
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
                new CameraRecord { Id = "cam-auth-fail", Ip = "192.168.1.40", Enabled = true, Username = "admin", Password = "bad" },
            },
        });

        var fakeService = new FakeOnvifDeviceManagementService
        {
            NtpHandler = (_, _) => OnvifOperationResult.Fail("auth", "device rejected credential"),
        };

        var dispatcher = BuildDispatcher(paths, settingsStore, cameraStore, fakeService);

        var result = dispatcher.Execute(CliCommandParser.Parse(new[] { "/usentp", "10.0.0.5" }));

        var loaded = cameraStore.Load(paths.CamerasFilePath);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error=auth", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, loaded.Cameras.Single().NtpServerIp);
    }

    private static CliCommandDispatcher BuildDispatcher(
        ApplicationDataPaths paths,
        YamlSettingsStore settingsStore,
        CameraListStore cameraStore,
        FakeOnvifDeviceManagementService? fakeService = null)
    {
        var runner = new FakeCommandRunner();
        var onvifService = fakeService ?? new FakeOnvifDeviceManagementService();
        return new CliCommandDispatcher(
            paths,
            settingsStore,
            cameraStore,
            new StubOnvifDiscoveryService(),
            onvifService,
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

    private sealed class FakeOnvifDeviceManagementService : IOnvifDeviceManagementService
    {
        public Func<CameraRecord, DateTimeOffset, OnvifOperationResult>? TimeUpdateHandler { get; init; }

        public Func<CameraRecord, string, OnvifOperationResult>? NtpHandler { get; init; }

        public Func<CameraRecord, OnvifOperationResult>? NtpModeHandler { get; init; }

        public Func<CameraRecord, OnvifDeviceInformationResult>? DeviceInformationHandler { get; init; }

        public Task<OnvifDeviceInformationResult> GetDeviceInformationAsync(
            CameraRecord camera,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = DeviceInformationHandler?.Invoke(camera) ?? OnvifDeviceInformationResult.Ok(string.Empty, string.Empty, string.Empty);
            return Task.FromResult(result);
        }

        public Task<OnvifOperationResult> SetSystemDateAndTimeAsync(
            CameraRecord camera,
            DateTimeOffset localNow,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = TimeUpdateHandler?.Invoke(camera, localNow) ?? OnvifOperationResult.Ok("fake time update ok");
            return Task.FromResult(result);
        }

        public Task<OnvifOperationResult> SetNtpServerAsync(
            CameraRecord camera,
            string ntpIp,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = NtpHandler?.Invoke(camera, ntpIp) ?? OnvifOperationResult.Ok("fake ntp update ok");
            return Task.FromResult(result);
        }

        public Task<OnvifOperationResult> SetTimeToNtpModeAsync(
            CameraRecord camera,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = NtpModeHandler?.Invoke(camera) ?? OnvifOperationResult.Ok("fake switch to ntp mode ok");
            return Task.FromResult(result);
        }
    }
}
