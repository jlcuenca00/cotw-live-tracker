using System.Security.Cryptography;

namespace CotwLiveTracker.Infrastructure;

internal static class ExecutableFingerprint
{
    public static string Sha256(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
