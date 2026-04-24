namespace IPCamClockSync.Core.Security;

public interface ICredentialProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
