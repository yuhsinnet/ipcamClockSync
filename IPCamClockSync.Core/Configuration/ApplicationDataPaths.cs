namespace IPCamClockSync.Core.Configuration;

public sealed class ApplicationDataPaths
{
    public string ConfigDirectory { get; init; } = string.Empty;

    public string SettingsFilePath { get; init; } = string.Empty;

    public string CamerasFilePath { get; init; } = string.Empty;

    public string GuiSettingsFilePath { get; init; } = string.Empty;

    public string ExportDirectory { get; init; } = string.Empty;

    public static ApplicationDataPaths Resolve(string? currentDirectory = null, string? appBaseDirectory = null)
    {
        var cwd = string.IsNullOrWhiteSpace(currentDirectory) ? Environment.CurrentDirectory : currentDirectory;
        var appBase = string.IsNullOrWhiteSpace(appBaseDirectory) ? AppContext.BaseDirectory : appBaseDirectory;
        var cwdConfig = Path.Combine(cwd, "config");
        var appConfig = Path.Combine(appBase, "config");

        var useCurrentDirectory = Directory.Exists(cwdConfig)
            || File.Exists(Path.Combine(cwdConfig, "settings.yaml"))
            || File.Exists(Path.Combine(cwdConfig, "cameras.json"));

        var configDirectory = useCurrentDirectory ? cwdConfig : appConfig;

        return new ApplicationDataPaths
        {
            ConfigDirectory = configDirectory,
            SettingsFilePath = Path.Combine(configDirectory, "settings.yaml"),
            CamerasFilePath = Path.Combine(configDirectory, "cameras.json"),
            GuiSettingsFilePath = Path.Combine(configDirectory, "consolegui.settings.json"),
            ExportDirectory = Path.Combine(configDirectory, "export"),
        };
    }
}
