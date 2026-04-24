using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IPCamClockSync.Core.Configuration;

public sealed class YamlSettingsStore
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public YamlSettingsStore()
    {
        _deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public AppSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return AppSettings.CreateDefault();
        }

        var yaml = File.ReadAllText(filePath);
        var settings = _deserializer.Deserialize<AppSettings>(yaml) ?? AppSettings.CreateDefault();
        settings.Normalize();
        return settings;
    }

    public void Save(string filePath, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = _serializer.Serialize(settings);
        File.WriteAllText(filePath, yaml);
    }
}
