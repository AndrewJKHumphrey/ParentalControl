using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ParentalControl.Core.Helpers;

/// <summary>
/// Encrypts/decrypts vault passwords using Windows DPAPI (LocalMachine scope).
/// Key material is derived from Windows machine credentials — nothing is stored.
/// Each call uses a fresh random salt, so identical plaintexts produce different ciphertexts.
/// </summary>
[SupportedOSPlatform("windows")]
public static class VaultCrypto
{
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText),
            null,
            DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(bytes);
    }

    public static string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(stored),
                null,
                DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return string.Empty; }
    }
}
