using Atendee.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Centralizes account disablement and session revocation for operational
/// identities. Security-stamp validation is configured on every request so
/// rotated stamps invalidate existing cookies on their next use.
/// </summary>
public sealed class OperationalAccountSecurityService(
    UserManager<OperationalIdentityUser> userManager) : IOperationalAccountSecurityService
{
    public async Task<bool> DisableAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
            return false;

        if (!user.LockoutEnabled)
        {
            var lockoutEnabled = await userManager.SetLockoutEnabledAsync(user, true);
            EnsureSucceeded(lockoutEnabled, "Could not enable account lockout.");
        }

        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        EnsureSucceeded(locked, "Could not disable the operational identity.");

        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "Could not revoke existing operational sessions.");

        return true;
    }

    public async Task<bool> RevokeSessionsAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
            return false;

        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "Could not revoke existing operational sessions.");
        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                message + " " + string.Join("; ", result.Errors.Select(error => error.Code)));
        }
    }
}
