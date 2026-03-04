using System.Security.Cryptography;
using System.Text;

namespace ParentalControl.Core.Helpers;

/// <summary>
/// AES-based obfuscation for API keys stored in the local SQLite database.
/// Prevents casual exposure of keys in plain-text DB files; not a substitute
/// for full secret management.
/// </summary>
public static class ApiKeyHelper
{
    // 256-bit AES key + 128-bit IV derived from app-specific constants
    private static readonly byte[] _key = SHA256.HashData(
        Encoding.UTF8.GetBytes("ParentalControl_RAWG_2026_v1"));
    private static readonly byte[] _iv = SHA256.HashData(
        Encoding.UTF8.GetBytes("PC_API_IV_Seed_2026"))[..16];

    private const string Prefix = "ENC:";

    /// <summary>Encrypt a plain-text API key for DB storage.</summary>
    public static string EncryptForStorage(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV  = _iv;
        var plain     = Encoding.UTF8.GetBytes(plainText);
        var encrypted = aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypt a value from DB storage.
    /// Values that don't start with <c>ENC:</c> are returned as-is
    /// (legacy plain-text rows) so migration can proceed gracefully.
    /// </summary>
    public static string DecryptFromStorage(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix)) return stored; // legacy plain-text
        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV  = _iv;
            var cipher    = Convert.FromBase64String(stored[Prefix.Length..]);
            var decrypted = aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return string.Empty; }
    }

    /// <summary>Returns true if the stored value is already encrypted.</summary>
    public static bool IsEncrypted(string stored) => stored.StartsWith(Prefix);
}
