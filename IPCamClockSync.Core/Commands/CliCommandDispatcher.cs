using System.Net;
using System.Net.Sockets;
using IPCamClockSync.Core.Configuration;
using IPCamClockSync.Core.Data;
using IPCamClockSync.Core.Discovery;
using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.Core.Commands;

public sealed class CliCommandDispatcher
{
    private readonly ApplicationDataPaths _paths;
    private readonly YamlSettingsStore _settingsStore;
    private readonly CameraListStore _cameraListStore;
    private readonly IOnvifDiscoveryService _discoveryService;
    private readonly NtpWindowsServiceController _service;
    private readonly WindowsFirewallController _firewall;

    public CliCommandDispatcher(
        ApplicationDataPaths paths,
        YamlSettingsStore settingsStore,
        CameraListStore cameraListStore,
        IOnvifDiscoveryService discoveryService,
        NtpWindowsServiceController service,
        WindowsFirewallController firewall)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _cameraListStore = cameraListStore;
        _discoveryService = discoveryService;
        _service = service;
        _firewall = firewall;
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!command.IsValid)
        {
            return new CommandExecutionResult { ExitCode = 2, Message = command.ErrorMessage ?? "Invalid command." };
        }

        return command.Group switch
        {
            CommandGroup.Help => new CommandExecutionResult { ExitCode = 0, Message = HelpText() },
            CommandGroup.Operate => ExecuteOperate(command),
            CommandGroup.Service => ExecuteService(command),
            CommandGroup.Firewall => ExecuteFirewall(command),
            _ => new CommandExecutionResult { ExitCode = 2, Message = "Unsupported command group." },
        };
    }

    private CommandExecutionResult ExecuteOperate(ParsedCommand command)
    {
        return command.Name switch
        {
            "scan" => ExecuteScan(),
            "update-once" => ExecuteUpdateOnce(),
            "set-ntp" => ExecuteSetNtp(command),
            "validate" => ExecuteValidate(),
            "export" => ExecuteExport(),
            _ => new CommandExecutionResult { ExitCode = 2, Message = "Unknown operate command." },
        };
    }

    private CommandExecutionResult ExecuteScan()
    {
        try
        {
            var settings = _settingsStore.Load(_paths.SettingsFilePath);
            var results = _discoveryService.DiscoverAsync(
                    new DiscoveryOptions
                    {
                        ProbeTimeoutSeconds = settings.Scan.DurationSeconds,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var document = DiscoveredCameraMapper.ToCameraList(results, settings.Scan.TimeoutSeconds);
            _cameraListStore.Save(
                _paths.CamerasFilePath,
                document,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            var lines = new List<string>
            {
                $"Scan completed. Found {document.Cameras.Count} camera(s).",
                $"Saved to: {_paths.CamerasFilePath}",
            };

            lines.AddRange(document.Cameras.Select(camera => $"- {camera.Id} {camera.Ip}:{camera.Port}"));
            return new CommandExecutionResult { ExitCode = 0, Message = string.Join(Environment.NewLine, lines) };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult { ExitCode = 1, Message = $"Scan failed: {ex.Message}" };
        }
    }

    private CommandExecutionResult ExecuteValidate()
    {
        try
        {
            var settings = _settingsStore.Load(_paths.SettingsFilePath);
            var document = _cameraListStore.Load(
                _paths.CamerasFilePath,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            return new CommandExecutionResult
            {
                ExitCode = 0,
                Message = $"Validation succeeded. cameras.json contains {document.Cameras.Count} camera(s).",
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult { ExitCode = 1, Message = $"Validation failed: {ex.Message}" };
        }
    }

    private CommandExecutionResult ExecuteExport()
    {
        try
        {
            Directory.CreateDirectory(_paths.ExportDirectory);
            var exportRoot = Path.Combine(_paths.ExportDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(exportRoot);

            var settingsTarget = Path.Combine(exportRoot, "settings.yaml");
            var camerasTarget = Path.Combine(exportRoot, "cameras.json");

            if (File.Exists(_paths.SettingsFilePath))
            {
                File.Copy(_paths.SettingsFilePath, settingsTarget, overwrite: true);
            }
            else
            {
                _settingsStore.Save(settingsTarget, AppSettings.CreateDefault());
            }

            if (File.Exists(_paths.CamerasFilePath))
            {
                File.Copy(_paths.CamerasFilePath, camerasTarget, overwrite: true);
            }
            else
            {
                _cameraListStore.Save(camerasTarget, CameraListDocument.CreateEmpty());
            }

            return new CommandExecutionResult
            {
                ExitCode = 0,
                Message = $"Export completed. Files written to: {exportRoot}",
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult { ExitCode = 1, Message = $"Export failed: {ex.Message}" };
        }
    }

    private CommandExecutionResult ExecuteUpdateOnce()
    {
        try
        {
            var settings = _settingsStore.Load(_paths.SettingsFilePath);
            var cameraDocument = _cameraListStore.Load(
                _paths.CamerasFilePath,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            var targets = cameraDocument.Cameras.Where(camera => camera.Enabled).ToList();
            if (targets.Count == 0)
            {
                return new CommandExecutionResult { ExitCode = 0, Message = "No enabled camera found. Nothing to update." };
            }

            var outcomes = targets.Select(camera => RunUpdatePrecheck(camera, settings)).ToList();
            var successCount = outcomes.Count(result => result.IsSuccess);
            var failCount = outcomes.Count - successCount;

            var lines = new List<string>
            {
                $"Update precheck completed. Success={successCount}, Failed={failCount}, Total={outcomes.Count}",
            };

            lines.AddRange(outcomes.Select(item =>
                item.IsSuccess
                    ? $"[OK] {item.CameraId} {item.Ip}:{item.Port} attempts={item.Attempts} message={item.Message}"
                    : $"[FAIL] {item.CameraId} {item.Ip}:{item.Port} attempts={item.Attempts} error={item.ErrorType} message={item.Message}"));

            return new CommandExecutionResult
            {
                ExitCode = failCount == 0 ? 0 : 1,
                Message = string.Join(Environment.NewLine, lines),
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult { ExitCode = 1, Message = $"Update failed: {ex.Message}" };
        }
    }

    private CommandExecutionResult ExecuteSetNtp(ParsedCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return new CommandExecutionResult { ExitCode = 2, Message = "Missing NTP server IP. Usage: /set-ntp <ntp-ip>" };
        }

        var ntpIp = command.Arguments[0];
        if (!IPAddress.TryParse(ntpIp, out _))
        {
            return new CommandExecutionResult { ExitCode = 2, Message = $"Invalid NTP IP: {ntpIp}" };
        }

        try
        {
            var settings = _settingsStore.Load(_paths.SettingsFilePath);
            var cameraDocument = _cameraListStore.Load(
                _paths.CamerasFilePath,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            var updated = 0;
            foreach (var camera in cameraDocument.Cameras.Where(camera => camera.Enabled))
            {
                camera.NtpServerIp = ntpIp;
                updated++;
            }

            _cameraListStore.Save(
                _paths.CamerasFilePath,
                cameraDocument,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            return new CommandExecutionResult
            {
                ExitCode = 0,
                Message = $"Set-NTP completed. Applied {ntpIp} to {updated} enabled camera(s).",
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult { ExitCode = 1, Message = $"Set-NTP failed: {ex.Message}" };
        }
    }

    private static CameraUpdatePrecheckResult RunUpdatePrecheck(CameraRecord camera, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(camera.Username) ||
            (string.IsNullOrWhiteSpace(camera.Password) && string.IsNullOrWhiteSpace(camera.PasswordEncrypted)))
        {
            return CameraUpdatePrecheckResult.Fail(
                camera,
                0,
                "auth",
                "Missing username/password for update.");
        }

        var maxAttempts = settings.TimeUpdate.RetryCount + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (settings.TimeUpdate.ReadSystemTimeBeforeEachUpdate)
            {
                _ = DateTimeOffset.Now;
            }

            try
            {
                var timeoutMs = Math.Max(1000, settings.TimeUpdate.RequestTimeoutSeconds * 1000);
                if (CanConnect(camera.Ip, camera.Port, timeoutMs))
                {
                    return CameraUpdatePrecheckResult.Success(
                        camera,
                        attempt,
                        "Connectivity precheck passed; ONVIF time-set transport will be integrated next.");
                }

                if (attempt == maxAttempts)
                {
                    return CameraUpdatePrecheckResult.Fail(
                        camera,
                        attempt,
                        "timeout",
                        "Timed out while connecting camera endpoint.");
                }
            }
            catch (OperationCanceledException)
            {
                if (attempt == maxAttempts)
                {
                    return CameraUpdatePrecheckResult.Fail(
                        camera,
                        attempt,
                        "timeout",
                        "Timed out while connecting camera endpoint.");
                }
            }
            catch (SocketException ex)
            {
                if (attempt == maxAttempts)
                {
                    return CameraUpdatePrecheckResult.Fail(camera, attempt, "network", ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    return CameraUpdatePrecheckResult.Fail(camera, attempt, "unknown", ex.Message);
                }
            }
        }

        return CameraUpdatePrecheckResult.Fail(camera, maxAttempts, "unknown", "Update precheck failed unexpectedly.");
    }

    private static bool CanConnect(string ip, int port, int timeoutMs)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);

        var connectTask = client.ConnectAsync(ip, port, cts.Token).AsTask();
        connectTask.Wait(cts.Token);
        return client.Connected;
    }

    private CommandExecutionResult ExecuteService(ParsedCommand command)
    {
        var runResult = (command.Action ?? string.Empty) switch
        {
            "install" => _service.Install(command.Arguments.FirstOrDefault() ?? "IPCamClockSync.NtpServer.exe"),
            "uninstall" => _service.Uninstall(),
            "start" => _service.Start(),
            "stop" => _service.Stop(),
            "restart" => _service.Restart(),
            "status" => _service.Status(),
            _ => new CommandRunResult { ExitCode = 2, Output = "Unsupported service action." },
        };

        return new CommandExecutionResult
        {
            ExitCode = runResult.ExitCode,
            Message = runResult.Output,
        };
    }

    private CommandExecutionResult ExecuteFirewall(ParsedCommand command)
    {
        var runResult = (command.Action ?? string.Empty) switch
        {
            "enable" => _firewall.Enable(),
            "disable" => _firewall.Disable(),
            "status" => _firewall.Status(),
            "repair" => _firewall.Repair(),
            "mode-open" => _firewall.SetOpenMode(),
            "mode-strict" => _firewall.SetStrictMode(),
            _ => new CommandRunResult { ExitCode = 2, Output = "Unsupported firewall action." },
        };

        return new CommandExecutionResult
        {
            ExitCode = runResult.ExitCode,
            Message = runResult.Output,
        };
    }

    private sealed record CameraUpdatePrecheckResult(
        string CameraId,
        string Ip,
        int Port,
        bool IsSuccess,
        int Attempts,
        string ErrorType,
        string Message)
    {
        public static CameraUpdatePrecheckResult Success(CameraRecord camera, int attempts, string message)
        {
            return new CameraUpdatePrecheckResult(camera.Id, camera.Ip, camera.Port, true, attempts, "none", message);
        }

        public static CameraUpdatePrecheckResult Fail(CameraRecord camera, int attempts, string errorType, string message)
        {
            return new CameraUpdatePrecheckResult(camera.Id, camera.Ip, camera.Port, false, attempts, errorType, message);
        }
    }

    private static string HelpText() =>
@"IPCamClockSync CLI

operate:
  /scan
  /a
  /set-ntp <ntp-ip>
  /validate
  /export

service:
  /ntpserver service install <path-to-exe>
  /ntpserver service uninstall
  /ntpserver start|stop|restart|status

firewall:
  /ntpserver firewall status|enable|disable|repair
    /ntpserver firewall mode open|strict

help:
  /h";
}
