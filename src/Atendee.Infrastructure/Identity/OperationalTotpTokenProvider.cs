using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// RFC 6238 TOTP provider for operational identities. A separate replay-guard
/// table and atomic compare-and-set prevent code reuse, including concurrent requests.
/// Server clocks must be synchronized with NTP.
/// </summary>
public sealed class OperationalTotpTokenProvider(
    AtendeeIdentityDbContext identityDb)
    : IUserTwoFactorTokenProvider<OperationalIdentityUser>
{
    private const long StepSeconds = 30;
    private const int AllowedAdjacentSteps = 1;

    public async Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<OperationalIdentityUser> manager,
        OperationalIdentityUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        return !string.IsNullOrWhiteSpace(key);
    }

    public async Task<string> GenerateAsync(
        string purpose,
        UserManager<OperationalIdentityUser> manager,
        OperationalIdentityUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("The operational identity has no TOTP authenticator key.");

        return CreateCode(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds);
    }

    public async Task<bool> ValidateAsync(
        string purpose,
        string token,
        UserManager<OperationalIdentityUser> manager,
        OperationalIdentityUser user)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var normalized = token.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character)))
            return false;

        var secret = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        long? acceptedStep = null;

        for (var offset = -AllowedAdjacentSteps; offset <= AllowedAdjacentSteps; offset++)
        {
            var candidateStep = currentStep + offset;
            if (candidateStep < 0)
                continue;

            var expected = CreateCode(secret, candidateStep);
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(expected),
                System.Text.Encoding.ASCII.GetBytes(normalized)))
            {
                acceptedStep = Math.Max(acceptedStep ?? 0, candidateStep);
            }
        }

        if (acceptedStep is null)
            return false;

        // The guard lives outside the AspNetUsers row so a later Identity
        // profile/security-stamp update cannot roll back the replay marker.
        await using var transaction =
            await identityDb.Database.BeginTransactionAsync();

        await identityDb.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO identity."TotpReplayGuards" ("UserId", "LastAcceptedStep")
            VALUES ({user.Id}, NULL)
            ON CONFLICT ("UserId") DO NOTHING
            """);

        var affected = await identityDb.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE identity."TotpReplayGuards"
            SET "LastAcceptedStep" = {acceptedStep.Value}
            WHERE "UserId" = {user.Id}
              AND ("LastAcceptedStep" IS NULL OR "LastAcceptedStep" < {acceptedStep.Value})
            """);

        await transaction.CommitAsync();
        return affected == 1;
    }

    private static string CreateCode(string base32Secret, long timeStep)
    {
        var key = DecodeBase32(base32Secret);
        Span<byte> message = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(message, checked((ulong)timeStep));

#pragma warning disable CA5350 // HMAC-SHA1 is required for RFC 6238 authenticator compatibility.
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350

        var offset = hash[^1] & 0x0f;
        var binary =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);

        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in value.TrimEnd('=').ToUpperInvariant())
        {
            var digit = alphabet.IndexOf(character);
            if (digit < 0)
                throw new FormatException("The TOTP authenticator key is not valid base32.");

            buffer = (buffer << 5) | digit;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xff));
            }
        }

        return output.ToArray();
    }
}
