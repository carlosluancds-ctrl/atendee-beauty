using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Atendee.Api.Authentication;

public static class OperationalAuthenticationEndpoints
{
    private static readonly Lazy<(OperationalIdentityUser User, string Hash)> DummyCredential =
        new(() =>
        {
            var user = new OperationalIdentityUser();
            var dummyPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var hasher = new PasswordHasher<OperationalIdentityUser>();
            return (user, hasher.HashPassword(user, dummyPassword));
        });

    public const string MfaSetupScheme = "Atendee.MfaSetup";
    public const string MfaSetupPolicy = "Atendee.MfaSetupOnly";

    public static IEndpointRouteBuilder MapOperationalAuthentication(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth");

        group.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new AntiforgeryResponse(tokens.RequestToken ?? string.Empty));
        }).AllowAnonymous().RequireRateLimiting("auth-antiforgery");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-login");

        group.MapPost("/mfa/challenge", CompleteMfaChallengeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-mfa");

        group.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization();

        group.MapGet("/me", (TrustedRequestContext current) =>
        {
            var tenantId = current.RequireTenantId();
            return Results.Ok(new CurrentOperationalUserResponse(
                current.ActorId!.Value,
                tenantId.ToString(),
                current.DisplayName!,
                current.Role!.Value.ToString(),
                current.TenantStatus!.Value.ToString()));
        }).RequireAuthorization();

        var setup = group.MapGroup("/mfa/enrollment")
            .RequireAuthorization(MfaSetupPolicy)
            .RequireRateLimiting("auth-mfa");

        setup.MapPost("/start", StartMfaEnrollmentAsync);
        setup.MapPost("/verify", VerifyMfaEnrollmentAsync);
        setup.MapPost("/cancel", (Delegate)CancelMfaEnrollmentAsync);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        UserManager<OperationalIdentityUser> userManager,
        IPasswordHasher<OperationalIdentityUser> passwordHasher,
        SignInManager<OperationalIdentityUser> signInManager,
        ITenantMembershipResolver membershipResolver)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Email.Length > 256 ||
            request.Password.Length > 1024)
        {
            return GenericLoginFailure();
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Perform a password-hash verification on unknown accounts as well,
            // reducing response-time differences that could aid enumeration.
            _ = passwordHasher.VerifyHashedPassword(
                DummyCredential.Value.User,
                DummyCredential.Value.Hash,
                request.Password);
            return GenericLoginFailure();
        }

        // Verify the password and account confirmation before querying tenant
        // membership. A known login with an incorrect password does not reveal
        // whether the identity has a salon membership.
        var passwordCheck = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!passwordCheck.Succeeded)
            return GenericLoginFailure();

        var membership = await membershipResolver.ResolveAsync(
            user.Id,
            httpContext.RequestAborted);

        if (membership is null ||
            !membership.IsActive ||
            membership.TenantStatus is TenantStatus.Closing or TenantStatus.Closed)
        {
            return GenericLoginFailure();
        }

        var privilegedRole = membership.Role is TenantRole.Owner or TenantRole.Manager;
        if (privilegedRole && !user.TwoFactorEnabled)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("atendee:auth_stage", "mfa_setup")
            };
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, MfaSetupScheme));

            await httpContext.SignInAsync(
                MfaSetupScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
                });

            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.Accepted(
                value: new LoginStateResponse("mfa_setup_required"));
        }

        if (user.TwoFactorEnabled)
        {
            // Identity creates its short-lived two-factor user-id cookie; the
            // full application cookie is issued only after the challenge.
            var signIn = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                isPersistent: false,
                lockoutOnFailure: true);

            httpContext.Response.Headers.CacheControl = "no-store";

            if (signIn.RequiresTwoFactor)
                return Results.Accepted(value: new LoginStateResponse("mfa_required"));

            if (!signIn.Succeeded)
                return GenericLoginFailure();
        }
        else
        {
            await signInManager.SignInWithClaimsAsync(
                user,
                isPersistent: false,
                additionalClaims: [new Claim("amr", "pwd")]);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new LoginStateResponse("authenticated"));
    }

    private static async Task<IResult> CompleteMfaChallengeAsync(
        MfaChallengeRequest request,
        HttpContext httpContext,
        SignInManager<OperationalIdentityUser> signInManager,
        UserManager<OperationalIdentityUser> userManager,
        ITenantMembershipResolver membershipResolver)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 64)
            return Results.Unauthorized();

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || await userManager.IsLockedOutAsync(user))
            return Results.Unauthorized();

        var membership = await membershipResolver.ResolveAsync(
            user.Id,
            httpContext.RequestAborted);

        if (membership is null ||
            !membership.IsActive ||
            membership.TenantStatus is TenantStatus.Closing or TenantStatus.Closed)
        {
            await signInManager.SignOutAsync();
            return Results.Unauthorized();
        }

        var code = request.Code.Trim();
        var valid = request.UseRecoveryCode
            ? (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
            : await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                code);

        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            return Results.Unauthorized();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // Issue the full application cookie only once and include the signed
        // MFA marker. The trusted-tenant middleware requires it for Owner and
        // Manager on every request.
        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            additionalClaims:
            [
                new Claim("amr", "pwd"),
                new Claim("amr", "mfa")
            ]);

        return Results.Ok(new LoginStateResponse("authenticated"));
    }

    private static async Task<IResult> StartMfaEnrollmentAsync(
        HttpContext httpContext,
        UserManager<OperationalIdentityUser> userManager,
        ITenantMembershipResolver membershipResolver)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        var user = await GetSetupUserAsync(httpContext, userManager);
        if (user is null || user.TwoFactorEnabled)
            return Results.Unauthorized();

        var membership = await membershipResolver.ResolveAsync(
            user.Id,
            httpContext.RequestAborted);

        if (membership is null ||
            !membership.IsActive ||
            membership.Role is not (TenantRole.Owner or TenantRole.Manager))
        {
            await httpContext.SignOutAsync(MfaSetupScheme);
            return Results.Forbid();
        }

        var sharedKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(sharedKey))
        {
            var reset = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            sharedKey = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(sharedKey))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        var issuer = UrlEncoder.Default.Encode("Atendee Beauty");
        var account = UrlEncoder.Default.Encode(user.Email ?? user.UserName ?? user.Id.ToString());
        var label = $"{issuer}:{account}";
        var authenticatorUri =
            $"otpauth://totp/{label}?secret={sharedKey}&issuer={issuer}&digits=6&period=30&algorithm=SHA1";

        // The TOTP secret is returned only for this authenticated five-minute
        // enrollment session. It is never written to logs or cached.
        return Results.Ok(new MfaEnrollmentStartResponse(sharedKey, authenticatorUri));
    }

    private static async Task<IResult> VerifyMfaEnrollmentAsync(
        MfaEnrollmentVerifyRequest request,
        HttpContext httpContext,
        UserManager<OperationalIdentityUser> userManager,
        SignInManager<OperationalIdentityUser> signInManager,
        ITenantMembershipResolver membershipResolver)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 16)
            return Results.BadRequest(new { error = "invalid_mfa_code" });

        var user = await GetSetupUserAsync(httpContext, userManager);
        if (user is null || user.TwoFactorEnabled)
            return Results.Unauthorized();

        var membership = await membershipResolver.ResolveAsync(
            user.Id,
            httpContext.RequestAborted);

        if (membership is null ||
            !membership.IsActive ||
            membership.Role is not (TenantRole.Owner or TenantRole.Manager))
        {
            await httpContext.SignOutAsync(MfaSetupScheme);
            return Results.Forbid();
        }

        if (!user.TwoFactorEnabled)
        {
            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.Code.Trim());

            if (!valid)
            {
                await userManager.AccessFailedAsync(user);
                if (await userManager.IsLockedOutAsync(user))
                {
                    await httpContext.SignOutAsync(MfaSetupScheme);
                    return Results.Unauthorized();
                }

                return Results.BadRequest(new { error = "invalid_mfa_code" });
            }

            await userManager.ResetAccessFailedCountAsync(user);
            var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (recoveryCodes is null || !recoveryCodes.Any())
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        await httpContext.SignOutAsync(MfaSetupScheme);
        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            additionalClaims:
            [
                new Claim("amr", "pwd"),
                new Claim("amr", "mfa")
            ]);

        // Recovery codes are displayed exactly once. The Identity token store
        // encrypts their persisted value using the configured Data Protection
        // key ring; the caller must store this response safely.
        return Results.Ok(new MfaEnrollmentCompleteResponse(recoveryCodes.ToArray()));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        SignInManager<OperationalIdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        await httpContext.SignOutAsync(MfaSetupScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelMfaEnrollmentAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(MfaSetupScheme);
        return Results.NoContent();
    }

    private static async Task<OperationalIdentityUser?> GetSetupUserAsync(
        HttpContext context,
        UserManager<OperationalIdentityUser> userManager)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId) || userId == Guid.Empty)
            return null;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
            return null;

        return user;
    }

    private static IResult GenericLoginFailure()
        => Results.Json(
            new { error = "invalid_credentials_or_account_state" },
            statusCode: StatusCodes.Status401Unauthorized);

    private sealed record LoginRequest(string Email, string Password);
    private sealed record MfaChallengeRequest(string Code, bool UseRecoveryCode = false);
    private sealed record MfaEnrollmentVerifyRequest(string Code);
    private sealed record AntiforgeryResponse(string RequestToken);
    private sealed record LoginStateResponse(string State);
    private sealed record MfaEnrollmentStartResponse(string SharedKey, string AuthenticatorUri);
    private sealed record MfaEnrollmentCompleteResponse(IReadOnlyCollection<string> RecoveryCodes);
    private sealed record CurrentOperationalUserResponse(
        Guid UserId,
        string TenantId,
        string DisplayName,
        string Role,
        string TenantStatus);
}
