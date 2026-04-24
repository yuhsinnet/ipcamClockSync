using IPCamClockSync.Core.Configuration;

namespace IPCamClockSync.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Load_MissingFile_ShouldReturnDefaults()
    {
        var store = new YamlSettingsStore();
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-settings-{Guid.NewGuid():N}.yaml");

        var settings = store.Load(path);

        Assert.Equal("zh-TW", settings.App.Language);
        Assert.Equal(5, settings.Scan.DurationSeconds);
        Assert.Equal("open", settings.Firewall.ProfileMode);
        Assert.Equal(20, settings.Logging.Rotation.MaxFileSizeMb);
    }

    [Fact]
    public void Load_WithMissingSections_ShouldBackfillDefaults()
    {
        var store = new YamlSettingsStore();
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-settings-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "app:\n  language: en-US\nscan:\n  durationSeconds: 9\n");

        var settings = store.Load(path);

        Assert.Equal("en-US", settings.App.Language);
        Assert.Equal(9, settings.Scan.DurationSeconds);
        Assert.Equal(15, settings.Scan.TimeoutSeconds);
        Assert.Equal("open", settings.Firewall.ProfileMode);
        Assert.Equal(10, settings.Logging.Rotation.MaxFileCount);
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTripImportantValues()
    {
        var store = new YamlSettingsStore();
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-settings-{Guid.NewGuid():N}.yaml");
        var settings = AppSettings.CreateDefault();
        settings.Scan.DurationSeconds = 12;
        settings.Firewall.ProfileMode = "strict";
        settings.Firewall.EnableCredentialEncryption = true;
        settings.Logging.Rotation.MaxFileCount = 3;

        store.Save(path, settings);
        var loaded = store.Load(path);

        Assert.Equal(12, loaded.Scan.DurationSeconds);
        Assert.Equal("strict", loaded.Firewall.ProfileMode);
        Assert.True(loaded.Firewall.EnableCredentialEncryption);
        Assert.Equal(3, loaded.Logging.Rotation.MaxFileCount);
    }
}
