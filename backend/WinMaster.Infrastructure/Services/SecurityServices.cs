using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using WinMaster.Core;
using WinMaster.Core.Interfaces;

namespace WinMaster.Infrastructure.Services;

/// <summary>Protects secrets (proxy passwords, API tokens) at rest via ASP.NET DataProtection.</summary>
public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;
    public EncryptionService(IDataProtectionProvider provider) => _protector = provider.CreateProtector("WinMaster.Secrets");

    public string Encrypt(string plaintext) => string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        try { return _protector.Unprotect(ciphertext); } catch { return ""; }
    }
    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= 4) return new string('*', value.Length);
        return value[..2] + new string('*', value.Length - 4) + value[^2..];
    }
}

/// <summary>PBKDF2 password hashing — no external dependency.</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16, KeySize = 32, Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        if (parts.Length != 3) return false;
        var iters = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, key.Length);
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}

/// <summary>
/// Generates WM-XXXX-XXXX-XXXX-XXXX keys and signs {key,hwid,expiresAt} with HMAC-SHA512.
/// The signed token lets the client verify a license offline (FR-5.3) within a grace window.
/// </summary>
public class LicenseSigner : ILicenseSigner
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32
    private readonly byte[] _signingKey;

    public LicenseSigner(string signingSecret)
        => _signingKey = Encoding.UTF8.GetBytes(signingSecret);

    public string GenerateKey(LicenseType type)
    {
        var raw = RandomNumberGenerator.GetBytes(10);
        var sb = new StringBuilder("WM-");
        for (int i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(Alphabet[raw[i % raw.Length] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    public string Sign(string key, string hwid, DateTime? expiresAt)
    {
        var payload = $"{key}|{hwid}|{expiresAt?.ToUniversalTime():O}";
        using var hmac = new HMACSHA512(_signingKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    public bool Verify(string key, string hwid, DateTime? expiresAt, string token)
    {
        var expected = Sign(key, hwid, expiresAt);
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(token ?? "");
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
