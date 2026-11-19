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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        group.MapPost("/activate", ActivateAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-activation");

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

        endpoints.MapGet("/api/v1/tenant", (TrustedRequestContext current) =>
        {
            var tenantId = current.RequireTenantId();
            return Results.Ok(new
            {
                tenantId = tenantId.ToString(),
                name = current.TenantName!,
                slug = current.TenantSlug!,
                timeZoneId = current.TenantTimeZoneId!,
                status = current.TenantStatus!.Value.ToString(),
                membership = new
                {
                    displayName = current.DisplayName!,
                    role = current.Role!.Value.ToString()
                }
            });
        }).RequireAuthorization();

        var setup = group.MapGroup("/mfa/enrollment")
            .RequireAuthorization(MfaSetupPolicy)
            .RequireRateLimiting("auth-mfa");

        setup.MapPost("/start", StartMfaEnrollmentAsync);
        setup.MapPost("/verify", VerifyMfaEnrollmentAsync);
        setup.MapPost("/cancel", (Delegate)CancelMfaEnrollmentAsync);

        return endpoints;
    }

    private static async Task<IResult> ActivateAsync(
        ActivationRequest request,
        HttpContext httpContext,
        OperationalIdentityActivationService activationService)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        var result = await activationService.ActivateAsync(
            request.Token,
            request.Password,
            httpContext.RequestAborted);

        return result switch
        {
            OperationalIdentityActivationResult.Activated =>
                Results.Ok(new ActivationStateResponse("activated_sign_in_required")),
            OperationalIdentityActivationResult.PasswordPolicyRejected =>
                Results.BadRequest(new { error = "password_policy_failed" }),
            _ => Results.BadRequest(new { error = "activation_invalid_or_expired" })
        };
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        UserManager<OperationalIdentityUser> userManager,
        AtendeeIdentityDbContext identityDb,
        IPasswordHasher<OperationalIdentityUser> passwordHasher,
        SignInManager<OperationalIdentityUser> signInManager,
        ITenantMembershipResolver membershipResolver,
        ISecurityAuditWriter auditWriter)
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

        // A failed password changes AccessFailedCount and writes a mandatory audit
        // event. Serialize attempts for this identity by locking its row before
        // refreshing the tracked user and reading lockout state. Without this lock,
        // concurrent requests can race on Identity's ConcurrencyStamp: one counter
        // update may be rejected as a concurrency conflict while its login failure
        // is still audited, weakening the five-attempt lockout threshold.
        // Keep the counter mutation and mandatory audit event in this transaction.
        // Commit before resolving membership, whose resolver opens its own short
        // transaction on this DbContext.
        await using (var passwordTransaction = await identityDb.Database.BeginTransactionAsync(
            httpContext.RequestAborted))
        {
            var connection = identityDb.Database.GetDbConnection();
            await using (var lockUser = connection.CreateCommand())
            {
                lockUser.Transaction = passwordTransaction.GetDbTransaction();
                lockUser.CommandText = """
                    SELECT "Id"
                      FROM identity."AspNetUsers"
                     WHERE "Id" = @user_id
                     FOR UPDATE
                    """;
                var userIdParameter = lockUser.CreateParameter();
                userIdParameter.ParameterName = "user_id";
                userIdParameter.Value = user.Id;
                lockUser.Parameters.Add(userIdParameter);

                if (await lockUser.ExecuteScalarAsync(httpContext.RequestAborted) is not Guid)
                    return GenericLoginFailure();
            }

            // FindByEmailAsync ran before the transaction. Refresh after acquiring
            // the row lock so a request queued behind another failed attempt sees
            // the committed counter, lockout end and concurrency stamp.
            await identityDb.Entry(user).ReloadAsync(httpContext.RequestAborted);
            var alreadyLockedOut = await userManager.IsLockedOutAsync(user);

            var passwordCheck = await signInManager.CheckPasswordSignInAsync(
                user,
                request.Password,
                lockoutOnFailure: true);

            if (!passwordCheck.Succeeded)
            {
                if (!alreadyLockedOut)
                {
                    await auditWriter.WriteAsync("operational_auth.login_failed",
                        null, user.Id, httpContext.RequestAborted);
                }

                await passwordTransaction.CommitAsync(httpContext.RequestAborted);
                return GenericLoginFailure();
            }

            await passwordTransaction.CommitAsync(httpContext.RequestAborted);
        }

        var membership = await membershipResolver.ResolveAsync(
            user.Id,
            httpContext.RequestAborted);

        // A provisioned identity without a salon membership may enroll MFA first.
        // This is the only path to creating the first Owner: the membership is
        // inserted later, after the database can verify TwoFactorEnabled = true.
        if (membership is null && !user.TwoFactorEnabled)
            return await BeginMfaSetupAsync(user, httpContext, auditWriter);

        if (membership is null ||
            !membership.IsActive ||
            !TenantAccessPolicy.IsAllowed(
                membership.TenantStatus, membership.Role, TenantCapability.Login))
        {
            await auditWriter.WriteAsync("operational_auth.login_failed",
                null, user.Id, httpContext.RequestAborted);
            return GenericLoginFailure();
        }

        var privilegedRole = membership.Role is TenantRole.Owner or TenantRole.Manager;
        if (privilegedRole && !user.TwoFactorEnabled)
            return await BeginMfaSetupAsync(user, httpContext, auditWriter);

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
            {
                // Password verified, challenge still pending: null actor, no
                // application cookie. A failed mandatory audit write fails the request.
                await auditWriter.WriteAsync("operational_auth.password_verified_mfa_pending",
                    null, user.Id, httpContext.RequestAborted);
                return Results.Accepted(value: new LoginStateResponse("mfa_required"));
            }

            if (!signIn.Succeeded)
                return GenericLoginFailure();
        }
        else
        {
            // Audit first: if the mandatory audit write fails, no session cookie
            // is issued and the request fails instead of succeeding silently.
            await auditWriter.WriteAsync("operational_auth.login_succeeded",
                user.Id, user.Id, httpContext.RequestAborted);
            await signInManager.SignInWithClaimsAsync(
                user,
                isPersistent: false,
                additionalClaims: [new Claim("amr", "pwd")]);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new LoginStateResponse("authenticated"));
    }

    private static async Task<IResult> BeginMfaSetupAsync(
        OperationalIdentityUser user,
        HttpContext httpContext,
        ISecurityAuditWriter auditWriter)
    {
        // The actor is intentionally null: password verification is not a
        // completed application authentication. Audit before issuing setup cookie.
        await auditWriter.WriteAsync("operational_auth.password_verified_mfa_pending",
            null, user.Id, httpContext.RequestAborted);

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
        return Results.Accepted(value: new LoginStateResponse("mfa_setup_required"));
    }

    private static async Task<IResult> CompleteMfaChallengeAsync(
        MfaChallengeRequest request,
        HttpContext httpContext,
        SignInManager<OperationalIdentityUser> signInManager,
        UserManager<OperationalIdentityUser> userManager,
        ITenantMembershipResolver membershipResolver,
        AtendeeIdentityDbContext identityDb,
        ISecurityAuditWriter auditWriter)
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
            !TenantAccessPolicy.IsAllowed(
                membership.TenantStatus, membership.Role, TenantCapability.Login))
        {
            await signInManager.SignOutAsync();
            return Results.Unauthorized();
        }

        var code = request.Code.Trim();
        if (request.UseRecoveryCode)
        {
            // Recovery-code redemption and its audit event are one atomic
            // identity-database transaction.
            await using var transaction = await identityDb.Database.BeginTransactionAsync(
                httpContext.RequestAborted);
            var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
            if (!redeemed.Succeeded)
            {
                await userManager.AccessFailedAsync(user);
                await auditWriter.WriteAsync("operational_mfa.challenge_failed",
                    null, user.Id, httpContext.RequestAborted);
                await transaction.CommitAsync(httpContext.RequestAborted);
                return Results.Unauthorized();
            }

            await userManager.ResetAccessFailedCountAsync(user);
            await auditWriter.WriteAsync("operational_mfa.recovery_code_redeemed",
                user.Id, user.Id, httpContext.RequestAborted);
            await transaction.CommitAsync(httpContext.RequestAborted);
        }
        else
        {
            // The TOTP provider owns a separate atomic replay-guard transaction.
            // Audit the successful factor in a new transaction after that guard commits.
            var validTotp = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                code);
            if (!validTotp)
            {
                // The failed-attempt counter and its mandatory audit event must
                // commit together, just like the recovery-code failure path.
                await using var failureTransaction = await identityDb.Database.BeginTransactionAsync(
                    httpContext.RequestAborted);
                await userManager.AccessFailedAsync(user);
                await auditWriter.WriteAsync("operational_mfa.challenge_failed",
                    null, user.Id, httpContext.RequestAborted);
                await failureTransaction.CommitAsync(httpContext.RequestAborted);
                return Results.Unauthorized();
            }

            await using var transaction = await identityDb.Database.BeginTransactionAsync(
                httpContext.RequestAborted);
            await userManager.ResetAccessFailedCountAsync(user);
            await auditWriter.WriteAsync("operational_mfa.totp_challenge_succeeded",
                user.Id, user.Id, httpContext.RequestAborted);
            await transaction.CommitAsync(httpContext.RequestAborted);
        }

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

        if (membership is not null &&
            (!membership.IsActive ||
             membership.Role is not (TenantRole.Owner or TenantRole.Manager) ||
             !TenantAccessPolicy.IsAllowed(
                 membership.TenantStatus, membership.Role, TenantCapability.Login)))
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
        ITenantMembershipResolver membershipResolver,
        AtendeeIdentityDbContext identityDb,
        ISecurityAuditWriter auditWriter)
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

        if (membership is not null &&
            (!membership.IsActive ||
             membership.Role is not (TenantRole.Owner or TenantRole.Manager) ||
             !TenantAccessPolicy.IsAllowed(
                 membership.TenantStatus, membership.Role, TenantCapability.Login)))
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
                // The failed-attempt counter and its audit event commit together.
                // Volume is bounded: the auth-mfa rate limiter applies, and once
                // lockout is reached GetSetupUserAsync rejects further attempts
                // before any event is written. The submitted code is never stored.
                await using (var failureTransaction = await identityDb.Database.BeginTransactionAsync(
                    httpContext.RequestAborted))
                {
                    await userManager.AccessFailedAsync(user);
                    await auditWriter.WriteAsync("operational_mfa.enrollment_failed",
                        null, user.Id, httpContext.RequestAborted);
                    await failureTransaction.CommitAsync(httpContext.RequestAborted);
                }

                if (await userManager.IsLockedOutAsync(user))
                {
                    await httpContext.SignOutAsync(MfaSetupScheme);
                    return Results.Unauthorized();
                }

                return Results.BadRequest(new { error = "invalid_mfa_code" });
            }

            await using var transaction = await identityDb.Database.BeginTransactionAsync(
                httpContext.RequestAborted);
            await userManager.ResetAccessFailedCountAsync(user);
            var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            if (recoveryCodes is null || !recoveryCodes.Any())
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            await auditWriter.WriteAsync("operational_mfa.enrolled", user.Id, user.Id,
                httpContext.RequestAborted);
            await transaction.CommitAsync(httpContext.RequestAborted);

            await httpContext.SignOutAsync(MfaSetupScheme);

            // Before the first salon membership exists, enrollment must not issue
            // an application cookie. A trusted provisioning transaction can now
            // attach this MFA-ready identity as the salon Owner.
            if (membership is null)
                return Results.Ok(new
                {
                    state = "mfa_enrolled_membership_pending",
                    recoveryCodes = recoveryCodes.ToArray()
                });

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

        return Results.Problem(statusCode: StatusCodes.Status409Conflict);

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

    private sealed record ActivationRequest(string? Token, string? Password);
    private sealed record ActivationStateResponse(string State);
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
