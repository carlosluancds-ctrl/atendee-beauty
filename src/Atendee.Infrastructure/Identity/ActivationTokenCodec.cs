using System.Security.Cryptography;
using System.Text;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Creates opaque, high-entropy activation tokens. Only the SHA-256 digest is persisted.
/// </summary>
public static class ActivationTokenCodec
{
    public static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] HashToken(string token)
        => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    public static bool IsValidFormat(string? token)
    {
        if (token is null || token.Length != 43)
            return false;

        foreach (var character in token)
        {
            if (!(character is >= 'A' and <= 'Z') &&
                !(character is >= 'a' and <= 'z') &&
                !(character is >= '0' and <= '9') &&
                character is not '-' and not '_')
                return false;
        }

        return true;
    }
}