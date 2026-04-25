using System.Text.Json;
using IPCamClockSync.Core.Security;

namespace IPCamClockSync.Core.Data;

public sealed class CameraListStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICredentialProtector? _credentialProtector;

    public CameraListStore(ICredentialProtector? credentialProtector = null)
    {
        _credentialProtector = credentialProtector;
    }

    public CameraListDocument Load(string filePath)
    {
        return Load(filePath, new CameraListStoreOptions());
    }

    public CameraListDocument Load(string filePath, CameraListStoreOptions options)
    {
        if (!File.Exists(filePath))
        {
            return CameraListDocument.CreateEmpty();
        }

        var json = File.ReadAllText(filePath);
        var document = JsonSerializer.Deserialize<CameraListDocument>(json, JsonOptions) ?? CameraListDocument.CreateEmpty();
        document.Normalize();
        Validate(document);
        ApplyDecryption(document, options);
        return document;
    }

    public void Save(string filePath, CameraListDocument document)
    {
        Save(filePath, document, new CameraListStoreOptions());
    }

    public void Save(string filePath, CameraListDocument document, CameraListStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Normalize();
        Validate(document);
        var clone = CloneDocument(document);
        ApplyEncryption(clone, options);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(clone, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public void Validate(CameraListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.Version.Equals("1.0", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported cameras.json version: {document.Version}");
        }

        var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpointSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var camera in document.Cameras)
        {
            if (string.IsNullOrWhiteSpace(camera.Id))
            {
                throw new InvalidDataException("Camera id is required.");
            }

            if (string.IsNullOrWhiteSpace(camera.Ip))
            {
                throw new InvalidDataException($"Camera '{camera.Id}' ip is required.");
            }

            if (camera.Port is <= 0 or > 65535)
            {
                throw new InvalidDataException($"Camera '{camera.Id}' port is out of range.");
            }

            if (camera.ConnectionTimeoutSeconds <= 0)
            {
                throw new InvalidDataException($"Camera '{camera.Id}' timeout must be greater than zero.");
            }

            if (!idSet.Add(camera.Id))
            {
                throw new InvalidDataException($"Duplicate camera id: {camera.Id}");
            }

            if (!endpointSet.Add(camera.EndpointKey))
            {
                throw new InvalidDataException($"Duplicate camera endpoint: {camera.EndpointKey}");
            }
        }
    }

    private void ApplyEncryption(CameraListDocument document, CameraListStoreOptions options)
    {
        foreach (var camera in document.Cameras)
        {
            if (!options.EnableCredentialEncryption || string.IsNullOrEmpty(camera.Password))
            {
                continue;
            }

            try
            {
                camera.PasswordEncrypted = ResolveCredentialProtector().Protect(camera.Password);
                camera.Password = string.Empty;
            }
            catch (Exception) when (options.AllowPlaintextFallback)
            {
                camera.PasswordEncrypted = string.Empty;
            }
        }
    }

    private void ApplyDecryption(CameraListDocument document, CameraListStoreOptions options)
    {
        foreach (var camera in document.Cameras)
        {
            if (!options.EnableCredentialEncryption || string.IsNullOrWhiteSpace(camera.PasswordEncrypted))
            {
                continue;
            }

            try
            {
                camera.Password = ResolveCredentialProtector().Unprotect(camera.PasswordEncrypted);
            }
            catch (Exception) when (options.AllowPlaintextFallback && !string.IsNullOrWhiteSpace(camera.Password))
            {
                // Keep plaintext password as fallback when encrypted value cannot be restored.
            }
        }
    }

    private static CameraListDocument CloneDocument(CameraListDocument source)
    {
        return new CameraListDocument
        {
            Version = source.Version,
            Cameras = source.Cameras.Select(camera => new CameraRecord
            {
                Id = camera.Id,
                Ip = camera.Ip,
                Port = camera.Port,
                Username = camera.Username,
                Password = camera.Password,
                PasswordEncrypted = camera.PasswordEncrypted,
                Enabled = camera.Enabled,
                ConnectionTimeoutSeconds = camera.ConnectionTimeoutSeconds,
                NtpServerIp = camera.NtpServerIp,
                Model = camera.Model,
            }).ToList(),
        };
    }

    private ICredentialProtector ResolveCredentialProtector()
    {
        if (_credentialProtector is not null)
        {
            return _credentialProtector;
        }

        return new Base64CredentialProtector();
    }
}
