using IPCamClockSync.Core.Runtime;
using IPCamClockSync.Core.Services;

namespace IPCamClockSync.ConsoleGui;

public sealed class ConsoleGuiApp
{
    private const int SettingBlinkIntervalMs = 420;

    private readonly string _settingsFilePath;
    private readonly WindowsFirewallController _firewallController;
    private ConsoleGuiSettings _settings;
    private int _mainMenuSelectedIndex = 0;
    private int _settingsMenuSelectedIndex = 0;

    public ConsoleGuiApp()
    {
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, "config", "consolegui.settings.json");
        _settings = ConsoleGuiSettings.Load(_settingsFilePath);
        _settings.FirewallProfileMode = NormalizeFirewallMode(_settings.FirewallProfileMode);
        _firewallController = new WindowsFirewallController(new ProcessCommandRunner());
    }

    public void Run()
    {
        ShowFirstRunContentIfNeeded();

        var mainItems = new[]
        {
            "掃描攝影機 (Phase 1 待實作)",
            "保存清單 (Phase 1 待實作)",
            "自動單次更新時間 (Phase 2 待實作)",
            "自動設定 NTP (Phase 2 待實作)",
            "設定",
            "離開",
        };

        while (true)
        {
            var selectedIndex = SelectFromMenu(
                title: "IPCamClockSync Console GUI",
                subtitle: "=========================",
                items: mainItems,
                keyHint: "使用 ↑/↓ 移動，Enter 確認，H 查看提示",
                initialSelectedIndex: _mainMenuSelectedIndex,
                allowHelpShortcut: true);

            if (selectedIndex == -1)
            {
                ShowHelp();
                continue;
            }

            _mainMenuSelectedIndex = selectedIndex;

            switch (selectedIndex)
            {
                case 0:
                    ShowPlaceholder("掃描攝影機", "下一階段將接上 ONVIF WS-Discovery 掃描與分頁清單。");
                    break;
                case 1:
                    ShowPlaceholder("保存清單", "下一階段將接上掃描結果多選保存與 cameras.json 寫入流程。");
                    break;
                case 2:
                    ShowPlaceholder("單次更新時間", "下一階段將接上攝影機清單讀取與逐台時間推送。");
                    break;
                case 3:
                    ShowPlaceholder("設定 NTP", "下一階段將接上攝影機 NTP 目標位址推送流程。");
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
        _settings.Save(_settingsFilePath);
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
                            selectedIndex = (selectedIndex - 1 + 8) % 8;
                            break;
                        case ConsoleKey.DownArrow:
                            selectedIndex = (selectedIndex + 1) % 8;
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
                                    _settings = working;
                                    _settings.FirewallProfileMode = NormalizeFirewallMode(_settings.FirewallProfileMode);
                                    SaveSettings();
                                    ApplyFirewallMode(_settings.FirewallProfileMode);
                                    _settingsMenuSelectedIndex = selectedIndex;
                                    return;
                                case 7:
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

        RenderActionLine("儲存併返回主選單", selectedIndex == 6, editMode == SettingEditMode.None);
        RenderActionLine("返回主選單 (不儲存)", selectedIndex == 7, editMode == SettingEditMode.None);

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

    private static int SelectFromMenu(
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
                    return selectedIndex;
                case ConsoleKey.H:
                    if (allowHelpShortcut)
                    {
                        return -1;
                    }
                    break;
            }
        }
    }

    private void ShowHelp()
    {
        Console.Clear();
        Console.WriteLine("操作提示");
        Console.WriteLine("--------");
        Console.WriteLine("- 主選單輸入 1-5 選擇功能");
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
        _settings.Save(_settingsFilePath);
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
}
