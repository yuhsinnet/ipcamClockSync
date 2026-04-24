namespace IPCamClockSync.Core.Configuration;

public sealed class AppSettings
{
    public AppSettingsApp App { get; set; } = new();

    public ScanSettings Scan { get; set; } = new();

    public TimeUpdateSettings TimeUpdate { get; set; } = new();

    public NtpServerSettings NtpServer { get; set; } = new();

    public FirewallSettings Firewall { get; set; } = new();

    public SecuritySettings Security { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }

    public void Normalize()
    {
        App ??= new AppSettingsApp();
        Scan ??= new ScanSettings();
        TimeUpdate ??= new TimeUpdateSettings();
        NtpServer ??= new NtpServerSettings();
        Firewall ??= new FirewallSettings();
        Security ??= new SecuritySettings();
        Logging ??= new LoggingSettings();

        App.Normalize();
        Scan.Normalize();
        TimeUpdate.Normalize();
        NtpServer.Normalize();
        Firewall.Normalize();
        Security.Normalize();
        Logging.Normalize();
    }
}

public sealed class AppSettingsApp
{
    public string Language { get; set; } = "zh-TW";

    public bool ShowDisclaimerNextTime { get; set; }

    public bool ShowInstructionsNextTime { get; set; }

    public void Normalize()
    {
        Language = string.IsNullOrWhiteSpace(Language) ? "zh-TW" : Language.Trim();
    }
}

public sealed class ScanSettings
{
    public int DurationSeconds { get; set; } = 5;

    public int TimeoutSeconds { get; set; } = 15;

    public void Normalize()
    {
        DurationSeconds = DurationSeconds <= 0 ? 5 : DurationSeconds;
        TimeoutSeconds = TimeoutSeconds <= 0 ? 15 : TimeoutSeconds;
    }
}

public sealed class TimeUpdateSettings
{
    public string Mode { get; set; } = "sequential";

    public int MaxConcurrency { get; set; } = 1;

    public bool ReadSystemTimeBeforeEachUpdate { get; set; } = true;

    public void Normalize()
    {
        Mode = string.IsNullOrWhiteSpace(Mode) ? "sequential" : Mode.Trim();
        MaxConcurrency = MaxConcurrency <= 0 ? 1 : MaxConcurrency;
    }
}

public sealed class NtpServerSettings
{
    public string BindAddress { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 123;

    public string StartupType { get; set; } = "delayed-auto";

    public string ServiceAccount { get; set; } = "LocalService";

    public void Normalize()
    {
        BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? "0.0.0.0" : BindAddress.Trim();
        Port = Port is <= 0 or > 65535 ? 123 : Port;
        StartupType = string.IsNullOrWhiteSpace(StartupType) ? "delayed-auto" : StartupType.Trim();
        ServiceAccount = string.IsNullOrWhiteSpace(ServiceAccount) ? "LocalService" : ServiceAccount.Trim();
    }
}

public sealed class FirewallSettings
{
    public string ProfileMode { get; set; } = "open";

    public string RemoteScope { get; set; } = "any";

    public bool KeepRuleOnUninstall { get; set; } = true;

    public void Normalize()
    {
        ProfileMode = ProfileMode.Equals("strict", StringComparison.OrdinalIgnoreCase) ? "strict" : "open";
        RemoteScope = string.IsNullOrWhiteSpace(RemoteScope) ? "any" : RemoteScope.Trim();
    }
}

public sealed class SecuritySettings
{
    public bool ObfuscatePasswordsWithBase64 { get; set; }

    public void Normalize()
    {
    }
}

public sealed class LoggingSettings
{
    public string Format { get; set; } = "jsonl";

    public List<string> Channels { get; set; } = new() { "app", "scan", "update", "ntp", "security" };

    public LogRotationSettings Rotation { get; set; } = new();

    public void Normalize()
    {
        Format = string.IsNullOrWhiteSpace(Format) ? "jsonl" : Format.Trim();
        Channels ??= new List<string>();
        if (Channels.Count == 0)
        {
            Channels.AddRange(new[] { "app", "scan", "update", "ntp", "security" });
        }

        Rotation ??= new LogRotationSettings();
        Rotation.Normalize();
    }
}

public sealed class LogRotationSettings
{
    public int MaxFileSizeMb { get; set; } = 20;

    public int MaxFileCount { get; set; } = 10;

    public void Normalize()
    {
        MaxFileSizeMb = MaxFileSizeMb <= 0 ? 20 : MaxFileSizeMb;
        MaxFileCount = MaxFileCount <= 0 ? 10 : MaxFileCount;
    }
}
