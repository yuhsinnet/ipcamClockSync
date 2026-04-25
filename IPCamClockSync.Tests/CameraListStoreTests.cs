using IPCamClockSync.Core.Data;
using IPCamClockSync.Core.Security;

namespace IPCamClockSync.Tests;

public sealed class CameraListStoreTests
{
    [Fact]
    public void Load_MissingFile_ShouldReturnEmptyDocument()
    {
        var store = new CameraListStore();
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-cameras-{Guid.NewGuid():N}.json");

        var document = store.Load(path);

        Assert.Equal("1.0", document.Version);
        Assert.Empty(document.Cameras);
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTripCameraRecord()
    {
        var store = new CameraListStore();
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-cameras-{Guid.NewGuid():N}.json");
        var document = new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord
                {
                    Id = "cam-001",
                    Ip = "192.168.1.10",
                    Port = 8080,
                    Model = "IPC-Model-01",
                    Username = "admin",
                    Password = "pass",
                    Enabled = true,
                    ConnectionTimeoutSeconds = 25,
                },
            },
        };

        store.Save(path, document);
        var loaded = store.Load(path);

        Assert.Single(loaded.Cameras);
        Assert.Equal("cam-001", loaded.Cameras[0].Id);
        Assert.Equal("192.168.1.10", loaded.Cameras[0].Ip);
        Assert.Equal(8080, loaded.Cameras[0].Port);
        Assert.Equal("IPC-Model-01", loaded.Cameras[0].Model);
        Assert.Equal(25, loaded.Cameras[0].ConnectionTimeoutSeconds);
    }

    [Fact]
    public void Validate_DuplicateIds_ShouldThrow()
    {
        var store = new CameraListStore();
        var document = new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "dup", Ip = "192.168.1.10" },
                new CameraRecord { Id = "dup", Ip = "192.168.1.11" },
            },
        };

        Assert.Throws<InvalidDataException>(() => store.Validate(document));
    }

    [Fact]
    public void Validate_DuplicateEndpoints_ShouldThrow()
    {
        var store = new CameraListStore();
        var document = new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "cam-1", Ip = "192.168.1.10", Port = 80 },
                new CameraRecord { Id = "cam-2", Ip = "192.168.1.10", Port = 80 },
            },
        };

        Assert.Throws<InvalidDataException>(() => store.Validate(document));
    }

    [Fact]
    public void Save_WithEncryptionEnabled_ShouldPersistEncryptedPasswordAndLoadBack()
    {
        var store = new CameraListStore(new Base64CredentialProtector());
        var path = Path.Combine(Path.GetTempPath(), $"ipcam-cameras-{Guid.NewGuid():N}.json");
        var options = new CameraListStoreOptions
        {
            EnableCredentialEncryption = true,
            AllowPlaintextFallback = false,
        };

        var document = new CameraListDocument
        {
            Cameras =
            {
                new CameraRecord { Id = "cam-001", Ip = "192.168.1.20", Username = "admin", Password = "secret" },
            },
        };

        store.Save(path, document, options);
        var rawJson = File.ReadAllText(path);
        var loaded = store.Load(path, options);

        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret")), rawJson);
        Assert.DoesNotContain("\"password\": \"secret\"", rawJson);
        Assert.Equal("secret", loaded.Cameras[0].Password);
    }
}
