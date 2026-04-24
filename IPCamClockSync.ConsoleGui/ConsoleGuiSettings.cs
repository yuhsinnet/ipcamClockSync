using System.Text.Json;

namespace IPCamClockSync.ConsoleGui;

public sealed class ConsoleGuiSettings
{
    public bool HasCompletedFirstRun { get; set; }

    public bool ShowDisclaimerNextTime { get; set; } = false;

    public bool ShowInstructionsNextTime { get; set; } = false;

    public int ScanDurationSeconds { get; set; } = 5;

    public int ConnectionTimeoutSeconds { get; set; } = 15;

    public int MaxConcurrency { get; set; } = 1;

    public static ConsoleGuiSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ConsoleGuiSettings();
        }

        var json = File.ReadAllText(filePath);
        var value = JsonSerializer.Deserialize<ConsoleGuiSettings>(json);
        return value ?? new ConsoleGuiSettings();
    }

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(filePath, json);
    }
}
