using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace IPCamClockSync.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        EnsureSupported();
        var input = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(input, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);

        EnsureSupported();
        var input = Convert.FromBase64String(protectedText);
        var plainBytes = ProtectedData.Unprotect(input, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI credential protection is supported on Windows only.");
        }
    }
}
