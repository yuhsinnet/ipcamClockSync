using System.Collections.Concurrent;
using System.Net;
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
    private readonly IOnvifDeviceManagementService _onvifDeviceService;
    private readonly NtpWindowsServiceController _service;
    private readonly WindowsFirewallController _firewall;

    public CliCommandDispatcher(
        ApplicationDataPaths paths,
        YamlSettingsStore settingsStore,
        CameraListStore cameraListStore,
        IOnvifDiscoveryService discoveryService,
        IOnvifDeviceManagementService onvifDeviceService,
        NtpWindowsServiceController service,
        WindowsFirewallController firewall)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _cameraListStore = cameraListStore;
        _discoveryService = discoveryService;
        _onvifDeviceService = onvifDeviceService;
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
            "use-ntp" => ExecuteUseNtp(command),
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

            var outcomes = RunCameraBatchAsync(
                    targets,
                    settings.TimeUpdate.MaxConcurrency,
                    camera => ExecuteUpdateForCameraWithRetryAsync(camera, settings, CancellationToken.None))
                .GetAwaiter()
                .GetResult();
            var successCount = outcomes.Count(result => result.IsSuccess);
            var failCount = outcomes.Count - successCount;

            var lines = new List<string>
            {
                $"Update completed. Success={successCount}, Failed={failCount}, Total={outcomes.Count}",
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

    private CommandExecutionResult ExecuteUseNtp(ParsedCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return new CommandExecutionResult { ExitCode = 2, Message = "Missing NTP server IP. Usage: /usentp <ntp-ip>" };
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

            var enabledCameras = cameraDocument.Cameras.Where(camera => camera.Enabled).ToList();
            var outcomes = RunCameraBatchAsync(
                    enabledCameras,
                    settings.TimeUpdate.MaxConcurrency,
                    camera => ExecuteUseNtpForCameraWithRetryAsync(camera, ntpIp, settings, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            foreach (var item in outcomes.Where(item => item.IsSuccess))
            {
                var camera = cameraDocument.Cameras.FirstOrDefault(c => c.Id.Equals(item.CameraId, StringComparison.OrdinalIgnoreCase));
                if (camera is not null)
                {
                    camera.NtpServerIp = ntpIp;
                }
            }

            _cameraListStore.Save(
                _paths.CamerasFilePath,
                cameraDocument,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = settings.Security.ObfuscatePasswordsWithBase64,
                });

            var successCount = outcomes.Count(item => item.IsSuccess);
            var failCount = outcomes.Count - successCount;
            var lines = new List<string>
            {
                $"Use-NTP completed. Success={successCount}, Failed={failCount}, Total={outcomes.Count}.",
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
            return new CommandExecutionResult { ExitCode = 1, Message = $"Use-NTP failed: {ex.Message}" };
        }
    }

    private async Task<List<CameraOperationResult>> RunCameraBatchAsync(
        IReadOnlyCollection<CameraRecord> cameras,
        int maxConcurrency,
        Func<CameraRecord, Task<CameraOperationResult>> operation)
    {
        if (cameras.Count == 0)
        {
            return new List<CameraOperationResult>();
        }

        var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var outputs = new ConcurrentBag<CameraOperationResult>();

        var tasks = cameras.Select(async camera =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                outputs.Add(await operation(camera).ConfigureAwait(false));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return outputs.OrderBy(item => item.CameraId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<CameraOperationResult> ExecuteUpdateForCameraWithRetryAsync(
        CameraRecord camera,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!HasCredential(camera))
        {
            return CameraOperationResult.Fail(camera, 0, "auth", "Missing username/password for update.");
        }

        var fixedNow = DateTimeOffset.Now;
        var maxAttempts = settings.TimeUpdate.RetryCount + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var localNow = settings.TimeUpdate.ReadSystemTimeBeforeEachUpdate ? DateTimeOffset.Now : fixedNow;
            var result = await _onvifDeviceService
                .SetSystemDateAndTimeAsync(camera, localNow, settings.TimeUpdate.RequestTimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                return CameraOperationResult.Success(camera, attempt, result.Message);
            }

            if (!ShouldRetry(result.ErrorType, attempt, maxAttempts))
            {
                return CameraOperationResult.Fail(camera, attempt, result.ErrorType, result.Message);
            }
        }

        return CameraOperationResult.Fail(camera, maxAttempts, "unknown", "Update failed without explicit error detail.");
    }

    private async Task<CameraOperationResult> ExecuteUseNtpForCameraWithRetryAsync(
        CameraRecord camera,
        string ntpIp,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!HasCredential(camera))
        {
            return CameraOperationResult.Fail(camera, 0, "auth", "Missing username/password for NTP push.");
        }

        var maxAttempts = settings.TimeUpdate.RetryCount + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var setNtpResult = await _onvifDeviceService
                .SetNtpServerAsync(camera, ntpIp, settings.TimeUpdate.RequestTimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (setNtpResult.Success)
            {
                var switchModeResult = await _onvifDeviceService
                    .SetTimeToNtpModeAsync(camera, settings.TimeUpdate.RequestTimeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);

                if (switchModeResult.Success)
                {
                    return CameraOperationResult.Success(camera, attempt, "NTP server applied and DateTimeType switched to NTP.");
                }

                if (!ShouldRetry(switchModeResult.ErrorType, attempt, maxAttempts))
                {
                    return CameraOperationResult.Fail(camera, attempt, switchModeResult.ErrorType, switchModeResult.Message);
                }

                continue;
            }

            if (!ShouldRetry(setNtpResult.ErrorType, attempt, maxAttempts))
            {
                return CameraOperationResult.Fail(camera, attempt, setNtpResult.ErrorType, setNtpResult.Message);
            }
        }

        return CameraOperationResult.Fail(camera, maxAttempts, "unknown", "Use-NTP failed without explicit error detail.");
    }

    private static bool HasCredential(CameraRecord camera)
    {
        return !string.IsNullOrWhiteSpace(camera.Username) &&
               (!string.IsNullOrWhiteSpace(camera.Password) || !string.IsNullOrWhiteSpace(camera.PasswordEncrypted));
    }

    private static bool ShouldRetry(string errorType, int attempt, int maxAttempts)
    {
        if (attempt >= maxAttempts)
        {
            return false;
        }

        return errorType is "timeout" or "network";
    }

    private CommandExecutionResult ExecuteService(ParsedCommand command)
    {
        if (string.Equals(command.Action, "cli-verify", StringComparison.Ordinal))
        {
            return ExecuteNtpCliVerify(command);
        }

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

    private CommandExecutionResult ExecuteNtpCliVerify(ParsedCommand command)
    {
        var computer = command.Arguments.Count >= 1 ? command.Arguments[0] : "127.0.0.1";

        var samples = 3;
        if (command.Arguments.Count >= 2)
        {
            if (!int.TryParse(command.Arguments[1], out samples) || samples < 1 || samples > 20)
            {
                return new CommandExecutionResult
                {
                    ExitCode = 2,
                    Message = "Invalid samples value. Usage: /ntpserver cli verify [computer] [samples], samples range: 1-20.",
                };
            }
        }

        var runResult = _service.VerifyViaStripChart(computer, samples);
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

    private sealed record CameraOperationResult(
        string CameraId,
        string Ip,
        int Port,
        bool IsSuccess,
        int Attempts,
        string ErrorType,
        string Message)
    {
        public static CameraOperationResult Success(CameraRecord camera, int attempts, string message)
        {
            return new CameraOperationResult(camera.Id, camera.Ip, camera.Port, true, attempts, "none", message);
        }

        public static CameraOperationResult Fail(CameraRecord camera, int attempts, string errorType, string message)
        {
            return new CameraOperationResult(camera.Id, camera.Ip, camera.Port, false, attempts, errorType, message);
        }
    }

    private static string HelpText() =>
@"IPCamClockSync CLI

operate:    (操作命令 — 相機掃描與設定)
  /scan                   - 掃描網路並儲存發現的攝影機到 cameras.json
    /a                      - 對所有啟用的攝影機執行一次手動 ONVIF 時間更新
    /usentp <ntp-ip>        - 對所有啟用的攝影機設定 NTP 並切換為 NTP 同步模式
    /set-ntp <ntp-ip>       - 相容別名，等同 /usentp
  /validate               - 驗證 cameras.json 是否有效
  /export                 - 匯出設定與 cameras.json 到 timestamp 資料夾

service:    (Windows 服務管理 — NTP Server)
  /ntpserver service install <path-to-exe>   - 安裝 NTP server 服務
  /ntpserver service uninstall               - 移除已安裝的服務
  /ntpserver start|stop|restart|status       - 啟動/停止/重啟/查詢服務狀態
    /ntpserver cli verify [computer] [samples] - 使用 w32tm stripchart 做功能驗證 (預設 127.0.0.1, 3)

firewall:   (防火牆管理 — 調整 NTP Server 所需規則)
  /ntpserver firewall status|enable|disable|repair
    /ntpserver firewall mode open|strict     - open: 開放模式 (允許所需通訊)
                                              - strict: 嚴格模式 (限制只允許必要的流量)

help:
  /h   - 顯示本說明（包含中文說明與範例用法）";
}
