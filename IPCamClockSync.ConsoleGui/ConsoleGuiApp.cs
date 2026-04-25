using System.Net;
using System.Text;
using IPCamClockSync.Core.Commands;
using IPCamClockSync.Core.Configuration;
using IPCamClockSync.Core.Data;
using IPCamClockSync.Core.Discovery;
using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.ConsoleGui;

public sealed class ConsoleGuiApp
{
    private const int SettingBlinkIntervalMs = 420;

    private readonly ApplicationDataPaths _paths;
    private readonly string _guiSettingsFilePath;
    private readonly WindowsFirewallController _firewallController;
    private readonly CliCommandDispatcher _cliDispatcher;
    private readonly YamlSettingsStore _settingsStore;
    private readonly CameraListStore _cameraListStore;
    private readonly IOnvifDiscoveryService _discoveryService;
    private readonly IOnvifDeviceManagementService _deviceManagementService;
    private AppSettings _appSettings;
    private CameraListDocument _cameraList;
    private CameraListDocument? _pendingScanDocument;
    private ConsoleGuiSettings _settings;
    private int _mainMenuSelectedIndex = 0;
    private int _settingsMenuSelectedIndex = 0;
    private int _scanNicSelectedIndex = 0;
    private int _saveListSelectedIndex = 0;
    private int _credentialMenuSelectedIndex = 0;
    private int _credentialCameraSelectedIndex = 0;

    public ConsoleGuiApp()
    {
        _paths = ApplicationDataPaths.Resolve();
        _guiSettingsFilePath = _paths.GuiSettingsFilePath;
        _settingsStore = new YamlSettingsStore();
        _cameraListStore = new CameraListStore();
        _discoveryService = new OnvifWsDiscoveryService();
        _deviceManagementService = new OnvifDeviceManagementService();
        _appSettings = _settingsStore.Load(_paths.SettingsFilePath);
        _cameraList = _cameraListStore.Load(
            _paths.CamerasFilePath,
            new CameraListStoreOptions
            {
                EnableCredentialEncryption = _appSettings.Security.ObfuscatePasswordsWithBase64,
            });
        _settings = ConsoleGuiSettings.Load(_guiSettingsFilePath);
        ApplyAppSettingsToGuiState(_appSettings, _settings);
        _firewallController = new WindowsFirewallController(new ProcessCommandRunner());
        _cliDispatcher = new CliCommandDispatcher(
            _paths,
            _settingsStore,
            _cameraListStore,
            _discoveryService,
            _deviceManagementService,
            new NtpWindowsServiceController(new ProcessCommandRunner()),
            _firewallController);
    }

    public void Run()
    {
        ShowFirstRunContentIfNeeded();

        var mainItems = new[]
        {
            "掃描攝影機",
            "保存清單",
            "單次更新時間 (/a)",
            "設定 NTP 並切換模式 (/usentp)",
            "設定",
            "離開",
        };

        while (true)
        {
            var menuResult = SelectFromMenu(
                title: "IPCamClockSync Console GUI V20260425",
                subtitle: "=========================",
                items: mainItems,
                keyHint: "使用 ↑/↓ 移動，Enter 確認，H 查看提示",
                initialSelectedIndex: _mainMenuSelectedIndex,
                allowHelpShortcut: true);

            if (menuResult.IsHelpRequested)
            {
                ShowHelp();
                continue;
            }

            if (menuResult.IsCanceled)
            {
                continue;
            }

            var selectedIndex = menuResult.SelectedIndex;

            _mainMenuSelectedIndex = selectedIndex;

            switch (selectedIndex)
            {
                case 0:
                    ShowScanWorkflow();
                    break;
                case 1:
                    SavePendingCameraList();
                    break;
                case 2:
                    ShowUpdateOnceWorkflow();
                    break;
                case 3:
                    ShowUseNtpWorkflow();
                    break;
                case 4:
                    ShowSettingsMenu();
                    break;
                case 5:
                    return;
            }
        }
    }

    private void ShowFirstRunContentIfNeeded()
    {
        if (!_settings.HasCompletedFirstRun || _settings.ShowDisclaimerNextTime)
        {
            Console.Clear();
            Console.WriteLine("軟體聲明");
            Console.WriteLine("--------");
            Console.WriteLine("本工具會修改攝影機時間與 NTP 設定，請先確認設備授權與維運流程。");
            Console.WriteLine();
            var next = AskShowNextTime("下次是否顯示本頁？(y/N，預設 N)");
            _settings.ShowDisclaimerNextTime = next;
        }

        if (!_settings.HasCompletedFirstRun || _settings.ShowInstructionsNextTime)
        {
            Console.Clear();
            Console.WriteLine("簡易說明");
            Console.WriteLine("--------");
            Console.WriteLine("建議流程：1) 掃描攝影機 -> 2) 建立清單 -> 3) 單次更新或設定 NTP");
            Console.WriteLine();
            var next = AskShowNextTime("下次是否顯示本頁？(y/N，預設 N)");
            _settings.ShowInstructionsNextTime = next;
        }

        _settings.HasCompletedFirstRun = true;
        SaveGuiSettings();
    }

    private void ShowSettingsMenu()
    {
        var working = CloneSettings(_settings);
        var selectedIndex = _settingsMenuSelectedIndex;
        var editMode = SettingEditMode.None;
        var blinkOn = true;
        var numericBuffer = string.Empty;
        var originalNumericValue = 0;
        var originalBoolValue = false;
        var originalFirewallMode = working.FirewallProfileMode;
        var lastBlinkAt = DateTime.UtcNow;
        var canControlCursor = OperatingSystem.IsWindows();
        var previousCursorVisible = true;

        Console.Clear();
        if (canControlCursor)
        {
            previousCursorVisible = Console.CursorVisible;
            Console.CursorVisible = false;
        }

        try
        {
            while (true)
            {
                RenderSettingsMenu(
                    working,
                    selectedIndex,
                    editMode,
                    blinkOn,
                    numericBuffer);

                if (editMode == SettingEditMode.None)
                {
                    var key = Console.ReadKey(intercept: true);
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow:
                            selectedIndex = (selectedIndex - 1 + 9) % 9;
                            break;
                        case ConsoleKey.DownArrow:
                            selectedIndex = (selectedIndex + 1) % 9;
                            break;
                        case ConsoleKey.Enter:
                            switch (selectedIndex)
                            {
                                case 0:
                                    editMode = SettingEditMode.Numeric;
                                    numericBuffer = working.ScanDurationSeconds.ToString();
                                    originalNumericValue = working.ScanDurationSeconds;
                                    break;
                                case 1:
                                    editMode = SettingEditMode.Numeric;
                                    numericBuffer = working.ConnectionTimeoutSeconds.ToString();
                                    originalNumericValue = working.ConnectionTimeoutSeconds;
                                    break;
                                case 2:
                                    editMode = SettingEditMode.Numeric;
                                    numericBuffer = working.MaxConcurrency.ToString();
                                    originalNumericValue = working.MaxConcurrency;
                                    break;
                                case 3:
                                    editMode = SettingEditMode.Boolean;
                                    originalBoolValue = working.ShowDisclaimerNextTime;
                                    break;
                                case 4:
                                    editMode = SettingEditMode.Boolean;
                                    originalBoolValue = working.ShowInstructionsNextTime;
                                    break;
                                case 5:
                                    editMode = SettingEditMode.FirewallProfile;
                                    originalFirewallMode = working.FirewallProfileMode;
                                    break;
                                case 6:
                                    editMode = SettingEditMode.Boolean;
                                    originalBoolValue = working.ObfuscatePasswordsWithBase64;
                                    break;
                                case 7:
                                    _settings = working;
                                    _settings.FirewallProfileMode = NormalizeFirewallMode(_settings.FirewallProfileMode);
                                    SaveSettings();
                                    ApplyFirewallMode(_settings.FirewallProfileMode);
                                    _settingsMenuSelectedIndex = selectedIndex;
                                    return;
                                case 8:
                                    _settingsMenuSelectedIndex = selectedIndex;
                                    return;
                            }

                            if (editMode != SettingEditMode.None)
                            {
                                blinkOn = true;
                                lastBlinkAt = DateTime.UtcNow;
                            }
                            break;
                    }

                    continue;
                }

                if ((DateTime.UtcNow - lastBlinkAt).TotalMilliseconds >= SettingBlinkIntervalMs)
                {
                    blinkOn = !blinkOn;
                    lastBlinkAt = DateTime.UtcNow;
                }

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(20);
                    continue;
                }

                var editKey = Console.ReadKey(intercept: true);
                if (editMode == SettingEditMode.Numeric)
                {
                    HandleNumericEditKey(
                        ref working,
                        selectedIndex,
                        ref editMode,
                        ref numericBuffer,
                        originalNumericValue,
                        editKey);
                    continue;
                }

                if (editMode == SettingEditMode.FirewallProfile)
                {
                    HandleFirewallProfileEditKey(
                        ref working,
                        ref editMode,
                        originalFirewallMode,
                        editKey);
                    continue;
                }

                HandleBooleanEditKey(
                    ref working,
                    selectedIndex,
                    ref editMode,
                    originalBoolValue,
                    editKey);
            }
        }
        finally
        {
            if (canControlCursor)
            {
                Console.CursorVisible = previousCursorVisible;
            }
        }
    }

    private static void HandleNumericEditKey(
        ref ConsoleGuiSettings working,
        int selectedIndex,
        ref SettingEditMode editMode,
        ref string numericBuffer,
        int originalValue,
        ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                if (TryParseNumericWithRange(selectedIndex, numericBuffer, out var parsed))
                {
                    SetNumericSetting(ref working, selectedIndex, parsed);
                    editMode = SettingEditMode.None;
                }
                break;
            case ConsoleKey.Escape:
                SetNumericSetting(ref working, selectedIndex, originalValue);
                editMode = SettingEditMode.None;
                break;
            case ConsoleKey.Backspace:
                if (numericBuffer.Length > 0)
                {
                    numericBuffer = numericBuffer[..^1];
                }
                break;
            default:
                if (char.IsDigit(key.KeyChar))
                {
                    numericBuffer += key.KeyChar;
                    if (int.TryParse(numericBuffer, out var candidate))
                    {
                        SetNumericSetting(ref working, selectedIndex, candidate);
                    }
                }
                break;
        }
    }

    private static void HandleBooleanEditKey(
        ref ConsoleGuiSettings working,
        int selectedIndex,
        ref SettingEditMode editMode,
        bool originalValue,
        ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                if (selectedIndex == 3)
                {
                    working.ShowDisclaimerNextTime = !working.ShowDisclaimerNextTime;
                }
                else if (selectedIndex == 4)
                {
                    working.ShowInstructionsNextTime = !working.ShowInstructionsNextTime;
                }
                else if (selectedIndex == 6)
                {
                    working.ObfuscatePasswordsWithBase64 = !working.ObfuscatePasswordsWithBase64;
                }
                break;
            case ConsoleKey.Enter:
                editMode = SettingEditMode.None;
                break;
            case ConsoleKey.Escape:
                if (selectedIndex == 3)
                {
                    working.ShowDisclaimerNextTime = originalValue;
                }
                else if (selectedIndex == 4)
                {
                    working.ShowInstructionsNextTime = originalValue;
                }
                else if (selectedIndex == 6)
                {
                    working.ObfuscatePasswordsWithBase64 = originalValue;
                }
                editMode = SettingEditMode.None;
                break;
        }
    }

    private static void HandleFirewallProfileEditKey(
        ref ConsoleGuiSettings working,
        ref SettingEditMode editMode,
        string originalValue,
        ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                working.FirewallProfileMode = ToggleFirewallMode(working.FirewallProfileMode);
                break;
            case ConsoleKey.Enter:
                working.FirewallProfileMode = NormalizeFirewallMode(working.FirewallProfileMode);
                editMode = SettingEditMode.None;
                break;
            case ConsoleKey.Escape:
                working.FirewallProfileMode = NormalizeFirewallMode(originalValue);
                editMode = SettingEditMode.None;
                break;
        }
    }

    private static void SetNumericSetting(ref ConsoleGuiSettings working, int selectedIndex, int value)
    {
        switch (selectedIndex)
        {
            case 0:
                working.ScanDurationSeconds = value;
                break;
            case 1:
                working.ConnectionTimeoutSeconds = value;
                break;
            case 2:
                working.MaxConcurrency = value;
                break;
        }
    }

    private static bool TryParseNumericWithRange(int selectedIndex, string value, out int parsed)
    {
        if (!int.TryParse(value, out parsed))
        {
            return false;
        }

        return selectedIndex switch
        {
            0 => parsed >= 1 && parsed <= 60,
            1 => parsed >= 1 && parsed <= 120,
            2 => parsed >= 1 && parsed <= 64,
            _ => false,
        };
    }

    private static void RenderSettingsMenu(
        ConsoleGuiSettings working,
        int selectedIndex,
        SettingEditMode editMode,
        bool blinkOn,
        string numericBuffer)
    {
        Console.SetCursorPosition(0, 0);
        WriteUiLine("設定");
        WriteUiLine("----");

        RenderSettingLine(
            "掃描時長 (秒)",
            selectedIndex == 0 && editMode == SettingEditMode.Numeric && numericBuffer.Length > 0 ? numericBuffer : working.ScanDurationSeconds.ToString(),
            isSelected: selectedIndex == 0,
            isEditingValue: selectedIndex == 0 && editMode == SettingEditMode.Numeric,
            blinkOn: blinkOn);

        RenderSettingLine(
            "連線逾時 (秒)",
            selectedIndex == 1 && editMode == SettingEditMode.Numeric && numericBuffer.Length > 0 ? numericBuffer : working.ConnectionTimeoutSeconds.ToString(),
            isSelected: selectedIndex == 1,
            isEditingValue: selectedIndex == 1 && editMode == SettingEditMode.Numeric,
            blinkOn: blinkOn);

        RenderSettingLine(
            "併發數",
            selectedIndex == 2 && editMode == SettingEditMode.Numeric && numericBuffer.Length > 0 ? numericBuffer : working.MaxConcurrency.ToString(),
            isSelected: selectedIndex == 2,
            isEditingValue: selectedIndex == 2 && editMode == SettingEditMode.Numeric,
            blinkOn: blinkOn);

        RenderSettingLine(
            "下次顯示聲明",
            ToBoolText(working.ShowDisclaimerNextTime),
            isSelected: selectedIndex == 3,
            isEditingValue: selectedIndex == 3 && editMode == SettingEditMode.Boolean,
            blinkOn: blinkOn);

        RenderSettingLine(
            "下次顯示說明",
            ToBoolText(working.ShowInstructionsNextTime),
            isSelected: selectedIndex == 4,
            isEditingValue: selectedIndex == 4 && editMode == SettingEditMode.Boolean,
            blinkOn: blinkOn);

        RenderSettingLine(
            "防火牆模式",
            NormalizeFirewallMode(working.FirewallProfileMode),
            isSelected: selectedIndex == 5,
            isEditingValue: selectedIndex == 5 && editMode == SettingEditMode.FirewallProfile,
            blinkOn: blinkOn);

        RenderSettingLine(
            "密碼非明碼儲存(base64)",
            ToBoolText(working.ObfuscatePasswordsWithBase64),
            isSelected: selectedIndex == 6,
            isEditingValue: selectedIndex == 6 && editMode == SettingEditMode.Boolean,
            blinkOn: blinkOn);

        RenderActionLine("儲存併返回主選單", selectedIndex == 7, editMode == SettingEditMode.None);
        RenderActionLine("返回主選單 (不儲存)", selectedIndex == 8, editMode == SettingEditMode.None);

        WriteUiLine(string.Empty);

        if (editMode == SettingEditMode.None)
        {
            WriteUiLine("[鍵盤提示] 使用 ↑/↓ 移動，Enter 確認");
            return;
        }

        if (selectedIndex is >= 0 and <= 2)
        {
            WriteUiLine("[鍵盤提示] 編輯中：輸入數字、Backspace 刪除，Enter 套用，Esc 取消");
            return;
        }

        WriteUiLine("[鍵盤提示] 編輯中：使用 ←/→ 切換，Enter 套用，Esc 取消");
    }

    private static void RenderSettingLine(string label, string value, bool isSelected, bool isEditingValue, bool blinkOn)
    {
        var width = Math.Max(1, Console.WindowWidth - 1);
        var prefix = $"{(isSelected ? "> " : "  ")}{label}: ";
        var maxValueLength = Math.Max(0, width - prefix.Length);
        var valueText = value.Length > maxValueLength ? value[..maxValueLength] : value;

        Console.Write(prefix);

        if (isEditingValue)
        {
            WriteBlinkingValue(valueText, blinkOn);
        }
        else if (isSelected)
        {
            WriteHighlighted(valueText);
        }
        else
        {
            Console.Write(valueText);
        }

        var remaining = width - prefix.Length - valueText.Length;
        if (remaining > 0)
        {
            Console.Write(new string(' ', remaining));
        }

        Console.WriteLine();
    }

    private static void RenderActionLine(string text, bool isSelected, bool canHighlight)
    {
        var width = Math.Max(1, Console.WindowWidth - 1);
        var line = isSelected ? $"> {text}" : $"  {text}";
        var trimmed = line.Length > width ? line[..width] : line;

        if (isSelected && canHighlight)
        {
            WriteHighlighted(trimmed);
        }
        else
        {
            Console.Write(trimmed);
        }

        var remaining = width - trimmed.Length;
        if (remaining > 0)
        {
            Console.Write(new string(' ', remaining));
        }

        Console.WriteLine();
    }

    private static void WriteBlinkingValue(string value, bool blinkOn)
    {
        if (blinkOn)
        {
            WriteHighlighted(value);
            return;
        }

        Console.Write(value);
    }

    private static void WriteHighlighted(string text)
    {
        var previousForeground = Console.ForegroundColor;
        var previousBackground = Console.BackgroundColor;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.BackgroundColor = ConsoleColor.Gray;
        Console.Write(text);
        Console.ForegroundColor = previousForeground;
        Console.BackgroundColor = previousBackground;
    }

    private static void WriteUiLine(string text)
    {
        var width = Math.Max(1, Console.WindowWidth - 1);
        var output = text.Length > width ? text[..width] : text.PadRight(width);
        Console.WriteLine(output);
    }

    private static ConsoleGuiSettings CloneSettings(ConsoleGuiSettings source)
    {
        return new ConsoleGuiSettings
        {
            HasCompletedFirstRun = source.HasCompletedFirstRun,
            ShowDisclaimerNextTime = source.ShowDisclaimerNextTime,
            ShowInstructionsNextTime = source.ShowInstructionsNextTime,
            ScanDurationSeconds = source.ScanDurationSeconds,
            ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds,
            MaxConcurrency = source.MaxConcurrency,
            FirewallProfileMode = NormalizeFirewallMode(source.FirewallProfileMode),
            ObfuscatePasswordsWithBase64 = source.ObfuscatePasswordsWithBase64,
        };
    }

    private static string ToBoolText(bool value) => value ? "true" : "false";

    private static string ToggleFirewallMode(string mode)
    {
        return NormalizeFirewallMode(mode) == "open" ? "strict" : "open";
    }

    private static string NormalizeFirewallMode(string mode)
    {
        return mode.Equals("strict", StringComparison.OrdinalIgnoreCase) ? "strict" : "open";
    }

    private void ApplyFirewallMode(string mode)
    {
        var normalized = NormalizeFirewallMode(mode);
        if (normalized == "strict")
        {
            _firewallController.SetStrictMode();
            return;
        }

        _firewallController.SetOpenMode();
    }

    private enum SettingEditMode
    {
        None,
        Numeric,
        Boolean,
        FirewallProfile,
    }

    private static MenuSelectionResult SelectFromMenu(
        string title,
        string subtitle,
        string[] items,
        string keyHint,
        int initialSelectedIndex = 0,
        bool allowHelpShortcut = false)
    {
        var selectedIndex = Math.Clamp(initialSelectedIndex, 0, items.Length - 1);

        while (true)
        {
            Console.Clear();
            Console.WriteLine(title);
            Console.WriteLine(subtitle);

            for (var i = 0; i < items.Length; i++)
            {
                var isSelected = i == selectedIndex;
                if (isSelected)
                {
                    var previousForeground = Console.ForegroundColor;
                    var previousBackground = Console.BackgroundColor;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"> {items[i]}");
                    Console.ForegroundColor = previousForeground;
                    Console.BackgroundColor = previousBackground;
                }
                else
                {
                    Console.WriteLine($"  {items[i]}");
                }
            }

            Console.WriteLine();
            PrintKeyHint(keyHint);

            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = (selectedIndex - 1 + items.Length) % items.Length;
                    break;
                case ConsoleKey.DownArrow:
                    selectedIndex = (selectedIndex + 1) % items.Length;
                    break;
                case ConsoleKey.Enter:
                    return MenuSelectionResult.Confirm(selectedIndex);
                case ConsoleKey.Escape:
                    return MenuSelectionResult.Cancel(selectedIndex);
                case ConsoleKey.H:
                    if (allowHelpShortcut)
                    {
                        return MenuSelectionResult.Help(selectedIndex);
                    }
                    break;
            }
        }
    }

    private readonly record struct MenuSelectionResult(int SelectedIndex, bool IsCanceled, bool IsHelpRequested)
    {
        public static MenuSelectionResult Confirm(int selectedIndex) => new(selectedIndex, false, false);

        public static MenuSelectionResult Cancel(int selectedIndex) => new(selectedIndex, true, false);

        public static MenuSelectionResult Help(int selectedIndex) => new(selectedIndex, false, true);
    }

    private void ShowHelp()
    {
        Console.Clear();
        Console.WriteLine("操作提示");
        Console.WriteLine("--------");
        Console.WriteLine("- 主選單使用 ↑/↓ 選擇功能");
        Console.WriteLine("- 『單次更新時間』等同 CLI /a");
        Console.WriteLine("- 『設定 NTP 並切換模式』等同 CLI /usentp <ntp-ip>");
        Console.WriteLine("- 輸入 h 可看提示");
        Console.WriteLine("- 設定頁可調整掃描時長、逾時與併發");
        Console.WriteLine();
        PrintKeyHint("按 Enter 返回");
        Console.ReadLine();
    }

    private static void ShowPlaceholder(string title, string content)
    {
        Console.Clear();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
        Console.WriteLine(content);
        Console.WriteLine();
        PrintKeyHint("按 Enter 返回");
        Console.ReadLine();
    }

    private void SaveSettings()
    {
        SaveGuiSettings();
        ApplyGuiStateToAppSettings(_settings, _appSettings);
        _settingsStore.Save(_paths.SettingsFilePath, _appSettings);
    }

    private void SaveGuiSettings()
    {
        _settings.Save(_guiSettingsFilePath);
    }

    private void ShowScanWorkflow()
    {
        // ── 網卡選擇 ──────────────────────────────────────────────────
        var nics = OnvifWsDiscoveryService.GetAvailableNetworkInterfaces();
        IReadOnlyList<IPAddress>? selectedBindAddresses = null;

        if (nics.Count > 1)
        {
            var nicItems = new string[nics.Count + 1];
            nicItems[0] = "全部網卡（自動偵測）";
            for (int i = 0; i < nics.Count; i++)
            {
                nicItems[i + 1] = $"{nics[i].Address}  [{nics[i].Name}]";
            }

            var nicResult = SelectFromMenu(
                title: "選擇掃描網卡",
                subtitle: "=============",
                items: nicItems,
                keyHint: "使用 ↑/↓ 移動，Enter 確認",
                initialSelectedIndex: _scanNicSelectedIndex);

            if (nicResult.IsCanceled)
            {
                return;
            }

            var nicIndex = nicResult.SelectedIndex;
            _scanNicSelectedIndex = nicIndex;

            if (nicIndex > 0)
            {
                selectedBindAddresses = new[] { nics[nicIndex - 1].Address };
            }
        }
        // ─────────────────────────────────────────────────────────────

        Console.Clear();
        Console.WriteLine("掃描攝影機");
        Console.WriteLine("--------");
        var nicLabel = selectedBindAddresses is { Count: > 0 } ? selectedBindAddresses[0].ToString() : "全部網卡";
        Console.WriteLine($"使用 WS-Discovery 掃描 {_settings.ScanDurationSeconds} 秒... 網卡：{nicLabel}");

        try
        {
            var results = _discoveryService.DiscoverAsync(
                    new DiscoveryOptions
                    {
                        ProbeTimeoutSeconds = _settings.ScanDurationSeconds,
                        BindAddresses = selectedBindAddresses,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _pendingScanDocument = DiscoveredCameraMapper.ToCameraList(results, _settings.ConnectionTimeoutSeconds);
            PopulateCameraModels(_pendingScanDocument).GetAwaiter().GetResult();

            Console.WriteLine();
            Console.WriteLine($"找到 {_pendingScanDocument.Cameras.Count} 台設備。");
            foreach (var camera in _pendingScanDocument.Cameras)
            {
                Console.WriteLine($"- {FormatCameraDisplay(camera)}");
            }

            if (_pendingScanDocument.Cameras.Count == 0)
            {
                Console.WriteLine("未收到 ONVIF ProbeMatch 回應。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"掃描失敗：{ex.Message}");
        }

        Console.WriteLine();
        PrintKeyHint("按 Enter 返回；若找到設備，可到『保存清單』寫入 cameras.json");
        WaitForReturnKey();
    }

    private void SavePendingCameraList()
    {
        if (_pendingScanDocument is null)
        {
            Console.Clear();
            Console.WriteLine("保存清單");
            Console.WriteLine("--------");
            Console.WriteLine("目前沒有待保存的掃描結果。請先執行『掃描攝影機』。");
            Console.WriteLine();
            PrintKeyHint("按 Enter 或 Esc 返回");
            WaitForReturnKey();
            return;
        }

        while (true)
        {
            var selected = PromptSelectCameras(_pendingScanDocument.Cameras);
            if (selected is null)
            {
                return;
            }

            if (selected.Count == 0)
            {
                Console.Clear();
                Console.WriteLine("保存清單");
                Console.WriteLine("--------");
                Console.WriteLine("未選擇任何設備，請重新勾選。\n");
                PrintKeyHint("按 Enter 或 Esc 返回選擇清單");
                WaitForReturnKey();
                continue;
            }

            var selectedDocument = BuildSelectedCameraDocument(selected);

            while (true)
            {
                if (!PromptCredentialWorkflow(selectedDocument.Cameras))
                {
                    break;
                }

                var targetPath = PromptSavePath(_paths.CamerasFilePath);
                if (targetPath is null)
                {
                    continue;
                }

                try
                {
                    _cameraListStore.Save(
                        targetPath,
                        selectedDocument,
                        new CameraListStoreOptions
                        {
                            EnableCredentialEncryption = _appSettings.Security.ObfuscatePasswordsWithBase64,
                        });

                    if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(_paths.CamerasFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        _cameraList = selectedDocument;
                    }

                    _pendingScanDocument = null;

                    Console.Clear();
                    Console.WriteLine("保存清單");
                    Console.WriteLine("--------");
                    Console.WriteLine($"已保存 {selectedDocument.Cameras.Count} 台設備到：");
                    Console.WriteLine(targetPath);
                    if (!string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(_paths.CamerasFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        Console.WriteLine("提醒：後續 /a 與 /usentp 仍會讀取預設路徑 config/cameras.json。");
                    }
                }
                catch (Exception ex)
                {
                    Console.Clear();
                    Console.WriteLine("保存清單");
                    Console.WriteLine("--------");
                    Console.WriteLine($"保存失敗：{ex.Message}");
                }

                Console.WriteLine();
                PrintKeyHint("按 Enter 或 Esc 返回");
                WaitForReturnKey();
                return;
            }
        }
    }

    private List<CameraRecord>? PromptSelectCameras(IReadOnlyList<CameraRecord> cameras)
    {
        if (cameras.Count == 0)
        {
            return new List<CameraRecord>();
        }

        var selectedIndex = Math.Clamp(_saveListSelectedIndex, 0, cameras.Count - 1);
        var selectedFlags = Enumerable.Repeat(true, cameras.Count).ToArray();

        while (true)
        {
            Console.Clear();
            Console.WriteLine("保存清單");
            Console.WriteLine("--------");
            Console.WriteLine("使用 ↑/↓ 移動，空白鍵切換 [v]/[ ]，Enter 確認，Esc 返回");
            Console.WriteLine();

            for (var i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                var marker = selectedFlags[i] ? "[v]" : "[ ]";
                var prefix = i == selectedIndex ? ">" : " ";
                Console.WriteLine($"{prefix} {marker} {FormatCameraDisplay(camera)}");
            }

            Console.WriteLine();
            Console.WriteLine($"已選擇 {selectedFlags.Count(flag => flag)} / {cameras.Count} 台");

            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = (selectedIndex - 1 + cameras.Count) % cameras.Count;
                    break;
                case ConsoleKey.DownArrow:
                    selectedIndex = (selectedIndex + 1) % cameras.Count;
                    break;
                case ConsoleKey.Spacebar:
                    selectedFlags[selectedIndex] = !selectedFlags[selectedIndex];
                    break;
                case ConsoleKey.Enter:
                    _saveListSelectedIndex = selectedIndex;
                    return cameras
                        .Where((_, index) => selectedFlags[index])
                        .ToList();
                case ConsoleKey.Escape:
                    _saveListSelectedIndex = selectedIndex;
                    return null;
            }
        }
    }

    private CameraListDocument BuildSelectedCameraDocument(IReadOnlyList<CameraRecord> selected)
    {
        var selectedDocument = CameraListDocument.CreateEmpty();

        foreach (var camera in selected)
        {
            var existing = _cameraList.Cameras.FirstOrDefault(item =>
                item.EndpointKey.Equals(camera.EndpointKey, StringComparison.OrdinalIgnoreCase));

            selectedDocument.Cameras.Add(new CameraRecord
            {
                Id = camera.Id,
                Ip = camera.Ip,
                Port = camera.Port,
                Model = string.IsNullOrWhiteSpace(camera.Model) ? existing?.Model ?? string.Empty : camera.Model,
                Username = existing?.Username ?? camera.Username,
                Password = existing?.Password ?? camera.Password,
                PasswordEncrypted = string.Empty,
                Enabled = existing?.Enabled ?? true,
                ConnectionTimeoutSeconds = existing?.ConnectionTimeoutSeconds ?? _settings.ConnectionTimeoutSeconds,
                NtpServerIp = existing?.NtpServerIp ?? camera.NtpServerIp,
            });
        }

        return selectedDocument;
    }

    private bool PromptCredentialWorkflow(IReadOnlyList<CameraRecord> cameras)
    {
        var items = new[]
        {
            "一次套用同一組帳密到全部已選設備",
            "逐台覆寫帳密",
            "完成並進入保存路徑",
        };

        while (true)
        {
            var result = SelectFromMenu(
                title: "帳密設定",
                subtitle: "========",
                items: items,
                keyHint: "使用 ↑/↓ 移動，Enter 確認，Esc 返回上一層",
                initialSelectedIndex: _credentialMenuSelectedIndex);

            _credentialMenuSelectedIndex = result.SelectedIndex;

            if (result.IsCanceled)
            {
                return false;
            }

            switch (result.SelectedIndex)
            {
                case 0:
                    PromptBulkCredentials(cameras);
                    break;
                case 1:
                    PromptPerCameraCredentials(cameras);
                    break;
                case 2:
                    return true;
            }
        }
    }

    private void PromptBulkCredentials(IReadOnlyList<CameraRecord> cameras)
    {
        Console.Clear();
        Console.WriteLine("批次套用帳密");
        Console.WriteLine("------------");
        Console.WriteLine($"將套用到 {cameras.Count} 台已選設備。直接 Enter = 保留原值，輸入 ! = 清空，Esc = 返回。\n");

        Console.Write("帳號: ");
        var usernameInput = ReadInteractiveInput();
        if (usernameInput.IsCanceled)
        {
            return;
        }

        Console.Write("密碼: ");
        var passwordInput = ReadInteractiveInput(maskInput: true);
        if (passwordInput.IsCanceled)
        {
            return;
        }

        foreach (var camera in cameras)
        {
            camera.Username = ApplyCredentialInput(camera.Username, usernameInput.Value);
            camera.Password = ApplyCredentialInput(camera.Password, passwordInput.Value);
        }

        Console.WriteLine();
        PrintKeyHint("已套用完成，按 Enter 或 Esc 返回帳密選單");
        WaitForReturnKey();
    }

    private void PromptPerCameraCredentials(IReadOnlyList<CameraRecord> cameras)
    {
        while (true)
        {
            var items = cameras
                .Select(camera => $"{FormatCameraDisplay(camera)} | user={FormatCredentialValue(camera.Username)} | pass={FormatPasswordValue(camera.Password)}")
                .ToArray();

            var result = SelectFromMenu(
                title: "逐台覆寫帳密",
                subtitle: "============",
                items: items,
                keyHint: "使用 ↑/↓ 移動，Enter 編輯，Esc 返回帳密選單",
                initialSelectedIndex: _credentialCameraSelectedIndex);

            _credentialCameraSelectedIndex = result.SelectedIndex;

            if (result.IsCanceled)
            {
                return;
            }

            EditSingleCameraCredential(cameras[result.SelectedIndex]);
        }
    }

    private void EditSingleCameraCredential(CameraRecord camera)
    {
        Console.Clear();
        Console.WriteLine("逐台覆寫帳密");
        Console.WriteLine("============");
        Console.WriteLine(FormatCameraDisplay(camera));
        Console.WriteLine("直接 Enter = 保留原值，輸入 ! = 清空，Esc = 返回上一層。\n");

        var nextUsername = camera.Username;
        var nextPassword = camera.Password;

        Console.Write($"帳號 [{FormatCredentialValue(camera.Username)}]: ");
        var usernameInput = ReadInteractiveInput();
        if (usernameInput.IsCanceled)
        {
            return;
        }

        nextUsername = ApplyCredentialInput(nextUsername, usernameInput.Value);

        Console.Write($"密碼 [{FormatPasswordValue(camera.Password)}]: ");
        var passwordInput = ReadInteractiveInput(maskInput: true);
        if (passwordInput.IsCanceled)
        {
            return;
        }

        nextPassword = ApplyCredentialInput(nextPassword, passwordInput.Value);

        camera.Username = nextUsername;
        camera.Password = nextPassword;
    }

    private static string ApplyCredentialInput(string currentValue, string input)
    {
        if (input.Equals("!", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(input))
        {
            return input;
        }

        return currentValue;
    }

    private static string FormatCredentialValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(空)" : value;
    }

    private static string FormatPasswordValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(空)" : "******";
    }

    private static InteractiveInputResult ReadInteractiveInput(bool maskInput = false)
    {
        var buffer = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return InteractiveInputResult.Confirm(buffer.ToString());
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return InteractiveInputResult.Cancel();
                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b");
                    }
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                        Console.Write(maskInput ? '*' : key.KeyChar);
                    }
                    break;
            }
        }
    }

    private static string? PromptSavePath(string defaultPath)
    {
        Console.Clear();
        Console.WriteLine("保存路徑");
        Console.WriteLine("--------");
        Console.WriteLine($"預設保存路徑：{defaultPath}");
        Console.WriteLine("直接 Enter 使用預設，Esc 返回帳密選單。\n");
        Console.Write("請輸入保存路徑: ");
        var inputResult = ReadInteractiveInput();
        if (inputResult.IsCanceled)
        {
            return null;
        }

        var input = inputResult.Value;
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultPath;
        }

        var candidate = input.Trim();
        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, candidate));
        }

        return candidate;
    }

    private async Task PopulateCameraModels(CameraListDocument document)
    {
        var candidates = document.Cameras
            .Where(camera => string.IsNullOrWhiteSpace(camera.Model))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        using var semaphore = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrency));
        var tasks = candidates.Select(async camera =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var model = await TryResolveCameraModelAsync(camera).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    camera.Model = model;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<string> TryResolveCameraModelAsync(CameraRecord camera)
    {
        var existing = _cameraList.Cameras.FirstOrDefault(item =>
            item.EndpointKey.Equals(camera.EndpointKey, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing?.Model))
        {
            return existing.Model;
        }

        var probeCamera = new CameraRecord
        {
            Id = camera.Id,
            Ip = camera.Ip,
            Port = camera.Port,
            Username = existing?.Username ?? string.Empty,
            Password = existing?.Password ?? string.Empty,
            ConnectionTimeoutSeconds = camera.ConnectionTimeoutSeconds,
        };

        var info = await _deviceManagementService.GetDeviceInformationAsync(
            probeCamera,
            Math.Max(1, camera.ConnectionTimeoutSeconds),
            CancellationToken.None).ConfigureAwait(false);

        return info.Success ? info.Model : string.Empty;
    }

    private static string FormatCameraDisplay(CameraRecord camera)
    {
        var model = string.IsNullOrWhiteSpace(camera.Model) ? "Unknown" : camera.Model;
        return $"{camera.Id} {camera.Ip}:{camera.Port} [{model}]";
    }

    private readonly record struct InteractiveInputResult(string Value, bool IsCanceled)
    {
        public static InteractiveInputResult Confirm(string value) => new(value, false);

        public static InteractiveInputResult Cancel() => new(string.Empty, true);
    }

    private void ShowUpdateOnceWorkflow()
    {
        Console.Clear();
        Console.WriteLine("單次更新時間");
        Console.WriteLine("----------");
        Console.WriteLine("將對 cameras.json 內所有啟用且有帳密的攝影機執行一次手動時間更新。");
        Console.WriteLine();
        if (!AskYesNo("是否開始執行？(y/N)", defaultYes: false))
        {
            return;
        }

        ExecuteAndShowOperateCommand(new[] { "/a" }, "單次更新完成。", shouldReloadCameraList: false);
    }

    private void ShowUseNtpWorkflow()
    {
        Console.Clear();
        Console.WriteLine("設定 NTP 並切換模式");
        Console.WriteLine("------------------");
        Console.WriteLine("將對 cameras.json 內所有啟用且有帳密的攝影機設定 NTP，並切換 DateTimeType 到 NTP。");
        Console.WriteLine();

        var suggested = _cameraList.Cameras.FirstOrDefault(camera => !string.IsNullOrWhiteSpace(camera.NtpServerIp))?.NtpServerIp;
        if (!string.IsNullOrWhiteSpace(suggested))
        {
            Console.WriteLine($"建議 NTP IP（來自現有清單）：{suggested}");
        }

        Console.Write("請輸入 NTP Server IP");
        if (!string.IsNullOrWhiteSpace(suggested))
        {
            Console.Write($"（直接 Enter 使用 {suggested}）");
        }

        Console.Write(": ");
        var input = ReadTrimmed();
        var ntpIp = string.IsNullOrWhiteSpace(input) ? suggested ?? string.Empty : input;
        if (!IPAddress.TryParse(ntpIp, out _))
        {
            Console.WriteLine();
            Console.WriteLine($"輸入格式錯誤：{ntpIp}");
            PrintKeyHint("按 Enter 返回");
            Console.ReadLine();
            return;
        }

        if (!AskYesNo($"確認對啟用設備下發 NTP={ntpIp}？(y/N)", defaultYes: false))
        {
            return;
        }

        ExecuteAndShowOperateCommand(new[] { "/usentp", ntpIp }, "NTP 設定完成。", shouldReloadCameraList: true);
    }

    private void ExecuteAndShowOperateCommand(string[] args, string successTitle, bool shouldReloadCameraList)
    {
        Console.Clear();
        Console.WriteLine(successTitle);
        Console.WriteLine(new string('-', successTitle.Length));

        var parsed = CliCommandParser.Parse(args);
        var result = _cliDispatcher.Execute(parsed);

        Console.WriteLine(result.Message);

        if (shouldReloadCameraList)
        {
            TryReloadCameraList();
        }

        Console.WriteLine();
        if (result.ExitCode == 0)
        {
            PrintKeyHint("按 Enter 返回");
        }
        else
        {
            PrintKeyHint("執行中有錯誤，請檢查訊息後按 Enter 返回");
        }

        Console.ReadLine();
    }

    private void TryReloadCameraList()
    {
        try
        {
            _appSettings = _settingsStore.Load(_paths.SettingsFilePath);
            _cameraList = _cameraListStore.Load(
                _paths.CamerasFilePath,
                new CameraListStoreOptions
                {
                    EnableCredentialEncryption = _appSettings.Security.ObfuscatePasswordsWithBase64,
                });
        }
        catch
        {
            // Ignore reload failures: result details are already shown by dispatcher.
        }
    }

    private static bool AskYesNo(string prompt, bool defaultYes)
    {
        Console.WriteLine(prompt);
        PrintKeyHint(defaultYes ? "輸入 y 或 n，直接 Enter 視為 y" : "輸入 y 或 n，直接 Enter 視為 n");
        var input = ReadTrimmed();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultYes;
        }

        return input.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAppSettingsToGuiState(AppSettings appSettings, ConsoleGuiSettings guiSettings)
    {
        guiSettings.ShowDisclaimerNextTime = appSettings.App.ShowDisclaimerNextTime;
        guiSettings.ShowInstructionsNextTime = appSettings.App.ShowInstructionsNextTime;
        guiSettings.ScanDurationSeconds = appSettings.Scan.DurationSeconds;
        guiSettings.ConnectionTimeoutSeconds = appSettings.Scan.TimeoutSeconds;
        guiSettings.MaxConcurrency = appSettings.TimeUpdate.MaxConcurrency;
        guiSettings.FirewallProfileMode = NormalizeFirewallMode(appSettings.Firewall.ProfileMode);
        guiSettings.ObfuscatePasswordsWithBase64 = appSettings.Security.ObfuscatePasswordsWithBase64;
    }

    private static void ApplyGuiStateToAppSettings(ConsoleGuiSettings guiSettings, AppSettings appSettings)
    {
        appSettings.App.ShowDisclaimerNextTime = guiSettings.ShowDisclaimerNextTime;
        appSettings.App.ShowInstructionsNextTime = guiSettings.ShowInstructionsNextTime;
        appSettings.Scan.DurationSeconds = guiSettings.ScanDurationSeconds;
        appSettings.Scan.TimeoutSeconds = guiSettings.ConnectionTimeoutSeconds;
        appSettings.TimeUpdate.MaxConcurrency = guiSettings.MaxConcurrency;
        appSettings.Firewall.ProfileMode = NormalizeFirewallMode(guiSettings.FirewallProfileMode);
        appSettings.Security.ObfuscatePasswordsWithBase64 = guiSettings.ObfuscatePasswordsWithBase64;
        appSettings.Normalize();
    }

    private static bool AskShowNextTime(string prompt)
    {
        Console.WriteLine(prompt);
        PrintKeyHint("輸入 y 或 n，直接 Enter 視為 n");
        var input = ReadTrimmed();
        return input.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTrimmed() => (Console.ReadLine() ?? string.Empty).Trim();

    private static void PrintKeyHint(string hint)
    {
        Console.WriteLine($"[鍵盤提示] {hint}");
    }

    private static void WaitForReturnKey()
    {
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape)
            {
                return;
            }
        }
    }
}
