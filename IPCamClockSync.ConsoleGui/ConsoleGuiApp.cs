namespace IPCamClockSync.ConsoleGui;

public sealed class ConsoleGuiApp
{
    private readonly string _settingsFilePath;
    private ConsoleGuiSettings _settings;

    public ConsoleGuiApp()
    {
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, "config", "consolegui.settings.json");
        _settings = ConsoleGuiSettings.Load(_settingsFilePath);
    }

    public void Run()
    {
        ShowFirstRunContentIfNeeded();

        while (true)
        {
            Console.Clear();
            Console.WriteLine("IPCamClockSync Console GUI");
            Console.WriteLine("=========================");
            Console.WriteLine("1. 掃描攝影機 (Phase 1 待實作)");
            Console.WriteLine("2. 自動單次更新時間 (Phase 2 待實作)");
            Console.WriteLine("3. 自動設定 NTP (Phase 2 待實作)");
            Console.WriteLine("4. 設定");
            Console.WriteLine("5. 離開");
            Console.WriteLine();
            PrintKeyHint("輸入 1-5 後 Enter，或輸入 h 查看命令提示");

            var input = ReadTrimmed();
            if (input.Equals("h", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                continue;
            }

            switch (input)
            {
                case "1":
                    ShowPlaceholder("掃描攝影機", "下一階段將接上 ONVIF WS-Discovery 掃描與分頁清單。");
                    break;
                case "2":
                    ShowPlaceholder("單次更新時間", "下一階段將接上攝影機清單讀取與逐台時間推送。");
                    break;
                case "3":
                    ShowPlaceholder("設定 NTP", "下一階段將接上攝影機 NTP 目標位址推送流程。");
                    break;
                case "4":
                    ShowSettingsMenu();
                    break;
                case "5":
                    return;
                default:
                    ShowPlaceholder("輸入錯誤", "請輸入 1-5 的選項。");
                    break;
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
        while (true)
        {
            Console.Clear();
            Console.WriteLine("設定");
            Console.WriteLine("----");
            Console.WriteLine($"1. 掃描時長 (秒): {_settings.ScanDurationSeconds}");
            Console.WriteLine($"2. 連線逾時 (秒): {_settings.ConnectionTimeoutSeconds}");
            Console.WriteLine($"3. 併發數: {_settings.MaxConcurrency}");
            Console.WriteLine($"4. 下次顯示聲明: {_settings.ShowDisclaimerNextTime}");
            Console.WriteLine($"5. 下次顯示說明: {_settings.ShowInstructionsNextTime}");
            Console.WriteLine("6. 返回主選單");
            Console.WriteLine();
            PrintKeyHint("輸入 1-6 後 Enter");

            var input = ReadTrimmed();
            switch (input)
            {
                case "1":
                    _settings.ScanDurationSeconds = AskInt("掃描時長秒數", _settings.ScanDurationSeconds, 1, 60);
                    SaveSettings();
                    break;
                case "2":
                    _settings.ConnectionTimeoutSeconds = AskInt("連線逾時秒數", _settings.ConnectionTimeoutSeconds, 1, 120);
                    SaveSettings();
                    break;
                case "3":
                    _settings.MaxConcurrency = AskInt("併發數", _settings.MaxConcurrency, 1, 64);
                    SaveSettings();
                    break;
                case "4":
                    _settings.ShowDisclaimerNextTime = !_settings.ShowDisclaimerNextTime;
                    SaveSettings();
                    break;
                case "5":
                    _settings.ShowInstructionsNextTime = !_settings.ShowInstructionsNextTime;
                    SaveSettings();
                    break;
                case "6":
                    return;
                default:
                    ShowPlaceholder("輸入錯誤", "請輸入 1-6 的選項。");
                    break;
            }
        }
    }

    private static int AskInt(string title, int currentValue, int min, int max)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine($"{title} 目前值: {currentValue}");
            Console.WriteLine($"請輸入 {min} 到 {max}，或直接 Enter 保持不變");
            PrintKeyHint("Enter 送出");
            var input = ReadTrimmed();

            if (string.IsNullOrWhiteSpace(input))
            {
                return currentValue;
            }

            if (int.TryParse(input, out var value) && value >= min && value <= max)
            {
                return value;
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
