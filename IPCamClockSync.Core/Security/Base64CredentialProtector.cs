using System.Text;

namespace IPCamClockSync.Core.Security;

public sealed class Base64CredentialProtector : ICredentialProtector
{
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        var bytes = Convert.FromBase64String(protectedText);
        return Encoding.UTF8.GetString(bytes);
    }
}