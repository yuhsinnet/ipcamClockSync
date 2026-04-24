using IPCamClockSync.Core.Security;

namespace IPCamClockSync.Core.Data;

public sealed class CameraListStoreOptions
{
    public bool EnableCredentialEncryption { get; init; }

    public bool AllowPlaintextFallback { get; init; } = true;

    public ICredentialProtector? CredentialProtector { get; init; }
}
