using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var identityConnectionString = Required("ATENDEE_TEST_IDENTITY_CONNECTION_STRING");
var tenantConnectionString = Required("ATENDEE_TEST_CONNECTION_STRING");
var migratorConnectionString = Required("ATENDEE_TEST_MIGRATOR_CONNECTION_STRING");
var keyRing = Path.Combine(Path.GetTempPath(), "atendee-http-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(keyRing);
var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var apiContentRoot = Path.Combine(repositoryRoot, "src", "Atendee.Api");
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? mfaFactory = null;
string? mfaKeyRing = null;
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? roleMatrixFactory = null;
string? roleMatrixKeyRing = null;
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? statusFactory = null;
string? statusKeyRing = null;

// Minimal hosting reads its default configuration while Program.cs executes;
// provide connection strings as process environment variables before the
// WebApplicationFactory starts the application.
Environment.SetEnvironmentVariable("ConnectionStrings__Identity", identityConnectionString);
Environment.SetEnvironmentVariable("ConnectionStrings__Tenant", tenantConnectionString);
Environment.SetEnvironmentVariable("DataProtection__KeyRingPath", keyRing);

var factory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
{
    web.UseContentRoot(apiContentRoot);
    web.UseEnvironment("Development");
    web.ConfigureAppConfiguration((_, configuration) =>
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = identityConnectionString,
            ["ConnectionStrings:Tenant"] = tenantConnectionString,
            ["DataProtection:KeyRingPath"] = keyRing
        });
    });
});

try
{
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    var email = "http-check-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string password = "Atendee-HTTP-Check-2026!";
    var tenantId = Guid.NewGuid();
    Guid userId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = new OperationalIdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(user, password);
        Ensure(created.Succeeded, "Synthetic HTTP identity is created.");
        userId = user.Id;
    }

    await SeedMembershipAsync(migratorConnectionString, tenantId, userId);

    var antiforgeryResponse = await client.GetAsync("/api/v1/auth/antiforgery");
    Ensure(antiforgeryResponse.StatusCode == HttpStatusCode.OK,
        "Antiforgery bootstrap endpoint is available.");
    var antiforgeryCookie = antiforgeryResponse.Headers.TryGetValues("Set-Cookie", out var bootstrapCookies)
        ? string.Join("; ", bootstrapCookies)
        : string.Empty;
    Ensure(HasCookieAttributes(antiforgeryCookie, "__Host-Atendee.AntiForgery"),
        "Antiforgery cookie uses __Host-, Secure, HttpOnly, Path=/ and SameSite=Strict.");

    var antiforgeryJson = await antiforgeryResponse.Content.ReadFromJsonAsync<JsonElement>();
    var requestToken = antiforgeryJson.GetProperty("requestToken").GetString();
    Ensure(!string.IsNullOrWhiteSpace(requestToken), "Antiforgery request token is issued.");

    using (var untrustedWebhook = new HttpRequestMessage(
        HttpMethod.Post, "/api/v1/webhooks/untrusted"))
    {
        untrustedWebhook.Content = JsonContent.Create(new { sample = true });
        var rejected = await client.SendAsync(untrustedWebhook);
        Ensure(rejected.StatusCode == HttpStatusCode.BadRequest,
            "Unsafe requests under webhook-like path prefixes do not bypass antiforgery.");
    }

    var unknownLogin = await PostWithCsrfAsync(client, "/api/v1/auth/login", requestToken!, new
    {
        email = "missing-" + Guid.NewGuid().ToString("N") + "@example.invalid",
        password
    });
    var unknownBody = await unknownLogin.Content.ReadAsStringAsync();
    var wrongPasswordLogin = await PostWithCsrfAsync(client, "/api/v1/auth/login", requestToken!, new
    {
        email,
        password = "Incorrect-HTTP-Password-2026!"
    });
    var wrongPasswordBody = await wrongPasswordLogin.Content.ReadAsStringAsync();
    Ensure(unknownLogin.StatusCode == HttpStatusCode.Unauthorized &&
           wrongPasswordLogin.StatusCode == HttpStatusCode.Unauthorized &&
           unknownBody == wrongPasswordBody,
        "Unknown identity and incorrect password return the same generic login response.");

    var spoofedActorId = Guid.NewGuid();
    var login = await PostWithCsrfAsync(client, "/api/v1/auth/login", requestToken!, new
    {
        email,
        password,
        actorId = spoofedActorId
    });
    Ensure(login.StatusCode == HttpStatusCode.OK,
        "Confirmed receptionist can authenticate with valid credentials.");
    var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(loginBody.GetProperty("state").GetString() == "authenticated",
        "Login returns authenticated state.");

    var authCookies = login.Headers.TryGetValues("Set-Cookie", out var issuedCookies)
        ? string.Join("; ", issuedCookies)
        : string.Empty;
    Ensure(HasCookieAttributes(authCookies, "__Host-Atendee.Auth"),
        "Application cookie uses __Host-, Secure, HttpOnly, Path=/ and SameSite=Strict.");

    var me = await client.GetAsync("/api/v1/auth/me");
    Ensure(me.StatusCode == HttpStatusCode.OK,
        "Authenticated HTTP session resolves the trusted tenant context.");
    var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(meBody.GetProperty("tenantId").GetString() == tenantId.ToString(),
        "Current-user endpoint returns the server-resolved tenant, not client input.");
    Ensure(meBody.GetProperty("role").GetString() == TenantRole.Receptionist.ToString(),
        "Current-user endpoint returns the persisted membership role.");

    var targetEmail = "http-audit-target-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    Guid targetUserId;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var target = new OperationalIdentityUser
        {
            UserName = targetEmail,
            Email = targetEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(target, password);
        Ensure(created.Succeeded, "Synthetic audit target identity is created.");
        targetUserId = target.Id;
    }

    await SeedAdditionalMembershipAsync(migratorConnectionString, tenantId, targetUserId, roleId: 3);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantMembershipResolver>();
        var membership = await resolver.ResolveAsync(userId, CancellationToken.None);
        Ensure(membership is not null, "Trusted actor membership is resolved from the database.");
        var trustedContext = scope.ServiceProvider.GetRequiredService<TrustedRequestContext>();
        trustedContext.Establish(membership!);

        var transactionRunner = scope.ServiceProvider.GetRequiredService<ITenantTransactionRunner>();
        var tenantDb = scope.ServiceProvider.GetRequiredService<Atendee.Infrastructure.Persistence.TenantDbContext>();
        await transactionRunner.ExecuteAsync(async cancellationToken =>
        {
            var affected = await tenantDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE atendee.tenant_users SET role = {(short)TenantRole.Professional} WHERE tenant_id = {tenantId} AND identity_user_id = {targetUserId}",
                cancellationToken);
            Ensure(affected == 1, "Trusted tenant transaction changes exactly one target membership.");
        });
    }

    var membershipRoleAudit = await ReadMembershipRoleChangeAuditAsync(migratorConnectionString, targetUserId);
    Ensure(membershipRoleAudit.Length == 1 &&
           membershipRoleAudit[0].EventType == "tenant_membership.role_changed" &&
           membershipRoleAudit[0].ActorId == userId &&
           membershipRoleAudit[0].TargetId == targetUserId &&
           membershipRoleAudit[0].TargetTenantId == tenantId &&
           membershipRoleAudit[0].ActorId != spoofedActorId,
        "Database trigger records the trusted actor and persisted target tenant, ignores client-supplied actorId, and writes exactly one membership event.");

    var forgedTenantId = Guid.NewGuid();
    _ = await InsertAuditEventWithForgedTenantAsync(identityConnectionString, targetUserId, forgedTenantId);
    var targetAuditTenants = await ReadAuditTenantTargetsAsync(migratorConnectionString, targetUserId);
    Ensure(targetAuditTenants.Length == 3 &&
           targetAuditTenants.All(entry => entry.TargetTenantId == tenantId),
        "Database trigger overwrites a forged TargetTenantId with the target's persisted tenant.");
    Ensure(await AuthLoginCannotReadTenantUsersAsync(identityConnectionString),
        "The authentication database role still has no direct SELECT access to tenant memberships.");
    Ensure(await AuthLoginCannotExecuteAuditTargetResolverAsync(identityConnectionString),
        "The authentication database role cannot execute the narrow audit-tenant resolver.");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        Ensure(await security.RevokeSessionsAsync(userId),
            "Session revocation service finds the operational identity.");
    }

    var revoked = await client.GetAsync("/api/v1/auth/me");
    Ensure(revoked.StatusCode == HttpStatusCode.Unauthorized,
        "Rotating the security stamp invalidates the existing cookie on the next request.");

    var nextAntiforgery = await client.GetAsync("/api/v1/auth/antiforgery");
    var nextToken = (await nextAntiforgery.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("requestToken").GetString();
    var secondLogin = await PostWithCsrfAsync(client, "/api/v1/auth/login", nextToken!, new
    {
        email,
        password
    });
    Ensure(secondLogin.StatusCode == HttpStatusCode.OK,
        "Identity can establish a fresh session after a session-only revocation.");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        Ensure(await security.DisableAsync(userId),
            "Account disablement service finds the operational identity.");
    }

    var auditTypes = await ReadSecurityAuditTypesAsync(migratorConnectionString, userId);
    Ensure(auditTypes.Length == 6 &&
           auditTypes.Count(type => type == "tenant_membership.created") == 1 &&
           auditTypes.Count(type => type == "operational_auth.login_failed") == 1 &&
           auditTypes.Count(type => type == "operational_auth.login_succeeded") == 2 &&
           auditTypes.Count(type => type == "operational_account.sessions_revoked") == 1 &&
           auditTypes.Count(type => type == "operational_account.disabled") == 1,
        "Login failure/success, session revocation and account disablement are durably recorded exactly once each.");
    Ensure(!auditTypes.Contains("operational_mfa.challenge_failed"),
        "Password-only accounts do not produce MFA challenge audit events.");

    var knownIdentityAuditTargets = await ReadAuditTenantTargetsAsync(migratorConnectionString, userId);
    Ensure(knownIdentityAuditTargets.Length == 6 &&
           knownIdentityAuditTargets.All(entry => entry.TargetTenantId == tenantId),
        "All identity-target audit events with a persisted membership receive that exact tenant ID.");

    var orphanEmail = "http-audit-orphan-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    Guid orphanUserId;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var orphan = new OperationalIdentityUser
        {
            UserName = orphanEmail,
            Email = orphanEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(orphan, password);
        Ensure(created.Succeeded, "Synthetic audit identity without tenant membership is created.");
        orphanUserId = orphan.Id;
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var auditWriter = scope.ServiceProvider.GetRequiredService<ISecurityAuditWriter>();
        await auditWriter.WriteAsync(
            "operational_auth.login_failed",
            null,
            orphanUserId,
            CancellationToken.None);
    }

    var orphanAuditTargets = await ReadAuditTenantTargetsAsync(migratorConnectionString, orphanUserId);
    Ensure(orphanAuditTargets.Length == 1 && orphanAuditTargets[0].TargetTenantId is null,
        "An identity without persisted membership keeps TargetTenantId NULL; no tenant is inferred.");

    var auditMutationDenied = false;
    try
    {
        await using var auditConnection = new NpgsqlConnection(identityConnectionString);
        await auditConnection.OpenAsync();
        await using var mutation = new NpgsqlCommand(
            @"UPDATE identity.""SecurityAuditEvents"" SET ""EventType"" = 'operational_account.disabled' WHERE ""TargetIdentityUserId"" = @target", auditConnection);
        mutation.Parameters.AddWithValue("target", userId);
        await mutation.ExecuteNonQueryAsync();
    }
    catch (PostgresException exception) when
        (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
    {
        auditMutationDenied = true;
    }
    Ensure(auditMutationDenied, "The runtime identity role cannot update or rewrite audit events.");

    var disabledSession = await client.GetAsync("/api/v1/auth/me");
    Ensure(disabledSession.StatusCode == HttpStatusCode.Unauthorized,
        "Account disablement invalidates an already-issued cookie on the next request.");

    var finalAntiforgery = await client.GetAsync("/api/v1/auth/antiforgery");
    var finalToken = (await finalAntiforgery.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("requestToken").GetString();
    var disabledLogin = await PostWithCsrfAsync(client, "/api/v1/auth/login", finalToken!, new
    {
        email,
        password
    });
    Ensure(disabledLogin.StatusCode == HttpStatusCode.Unauthorized,
        "Disabled operational identity cannot establish a new session.");
    var auditTypesAfterLockedOutAttempt = await ReadSecurityAuditTypesAsync(migratorConnectionString, userId);
    Ensure(auditTypesAfterLockedOutAttempt.Length == auditTypes.Length,
        "Login attempts against an already locked-out account do not grow the audit ledger.");

    // Run privileged-account MFA checks in a fresh host so the independent
    // per-IP login limiter starts with an isolated budget.
    mfaKeyRing = Path.Combine(Path.GetTempPath(), "atendee-mfa-http-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(mfaKeyRing);
    mfaFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = mfaKeyRing
            });
        });
    });

    using var mfaClient = mfaFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    var ownerEmail = "http-mfa-owner-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string ownerPassword = "Atendee-MFA-HTTP-2026!";
    var ownerTenantId = Guid.NewGuid();
    Guid ownerUserId;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var owner = new OperationalIdentityUser
        {
            UserName = ownerEmail,
            Email = ownerEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(owner, ownerPassword);
        Ensure(created.Succeeded, "Synthetic owner identity is created.");
        ownerUserId = owner.Id;
    }

    // The Owner is not attached to a salon until MFA enrollment is complete.

    var mfaAntiforgeryResponse = await mfaClient.GetAsync("/api/v1/auth/antiforgery");
    var mfaRequestToken = (await mfaAntiforgeryResponse.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("requestToken").GetString();
    Ensure(mfaAntiforgeryResponse.StatusCode == HttpStatusCode.OK &&
           !string.IsNullOrWhiteSpace(mfaRequestToken),
        "MFA test client obtains an antiforgery token.");

    var ownerLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", mfaRequestToken!, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    var ownerLoginBody = await ownerLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerLogin.StatusCode == HttpStatusCode.Accepted &&
           ownerLoginBody.GetProperty("state").GetString() == "mfa_setup_required",
        "Owner without MFA receives only a short-lived MFA-setup session.");
    var pendingSetupAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(pendingSetupAudit.Count(type => type == "operational_auth.password_verified_mfa_pending") == 1 &&
           !pendingSetupAudit.Contains("operational_auth.login_succeeded") &&
           !pendingSetupAudit.Contains("operational_auth.login_failed"),
        "Password verification that still requires MFA setup is audited as pending, not as a successful login.");

    var enrollmentStart = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/enrollment/start", mfaRequestToken!, new { });
    Ensure(enrollmentStart.StatusCode == HttpStatusCode.OK,
        "Owner can start authenticator enrollment within the setup session.");
    var enrollmentBody = await enrollmentStart.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(!string.IsNullOrWhiteSpace(enrollmentBody.GetProperty("sharedKey").GetString()) &&
           enrollmentBody.GetProperty("authenticatorUri").GetString()!.StartsWith("otpauth://totp/",
               StringComparison.Ordinal),
        "MFA enrollment returns an authenticator secret and provisioning URI.");

    string enrollmentCode;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var owner = await users.FindByIdAsync(ownerUserId.ToString());
        Ensure(owner is not null, "Owner identity remains available for TOTP verification.");
        enrollmentCode = await users.GenerateTwoFactorTokenAsync(
            owner!, users.Options.Tokens.AuthenticatorTokenProvider);
    }

    var auditBeforeInvalidEnrollment = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    var blankEnrollment = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", mfaRequestToken!, new { code = " " });
    Ensure(blankEnrollment.StatusCode == HttpStatusCode.BadRequest &&
           (await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId)).Length ==
               auditBeforeInvalidEnrollment.Length,
        "Malformed enrollment input is rejected without growing the audit ledger.");

    var wrongEnrollmentCode = enrollmentCode == "000000" ? "000001" : "000000";
    var invalidEnrollment = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", mfaRequestToken!, new
        {
            code = wrongEnrollmentCode
        });
    Ensure(invalidEnrollment.StatusCode == HttpStatusCode.BadRequest,
        "An invalid authenticator code is rejected during MFA enrollment.");
    var enrollmentFailureAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(enrollmentFailureAudit.Count(type => type == "operational_mfa.enrollment_failed") == 1 &&
           !enrollmentFailureAudit.Contains("operational_mfa.enrolled") &&
           !enrollmentFailureAudit.Contains("operational_mfa.challenge_failed"),
        "A failed MFA enrollment attempt is durably audited exactly once and does not enable MFA.");
    Ensure(await CountAuditEventsWithActorAsync(migratorConnectionString, ownerUserId,
               "operational_mfa.enrollment_failed", "operational_auth.password_verified_mfa_pending") == 0,
        "Pending-MFA and enrollment-failure events carry no actor: the identity is not yet authenticated.");

    var enrollmentVerify = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", mfaRequestToken!, new
        {
            code = enrollmentCode
        });
    if (enrollmentVerify.StatusCode != HttpStatusCode.OK)
    {
        var failureBody = await enrollmentVerify.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            "FAIL: Valid TOTP enrollment returned HTTP " + (int)enrollmentVerify.StatusCode +
            " with response: " + failureBody);
    }
    var enrollmentCompleteBody = await enrollmentVerify.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(enrollmentCompleteBody.GetProperty("state").GetString() == "mfa_enrolled_membership_pending",
        "A provisioned identity can complete MFA before receiving its first salon membership.");
    var recoveryCodes = enrollmentCompleteBody.GetProperty("recoveryCodes").EnumerateArray()
        .Select(value => value.GetString()!).ToArray();
    Ensure(recoveryCodes.Length == 10 && recoveryCodes.All(code => !string.IsNullOrWhiteSpace(code)),
        "MFA enrollment returns ten one-time recovery codes.");
    var enrollmentAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(enrollmentAudit.Contains("operational_mfa.enrolled"),
        "Successful MFA enrollment is durably audited.");

    var ownerMeBeforeMembership = await mfaClient.GetAsync("/api/v1/auth/me");
    Ensure(ownerMeBeforeMembership.StatusCode == HttpStatusCode.Unauthorized,
        "Pre-membership MFA enrollment does not issue an application session.");

    await SeedMembershipAsync(migratorConnectionString, ownerTenantId, ownerUserId, roleId: 1);
    var ownerFirstLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", mfaRequestToken!, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    var ownerFirstLoginBody = await ownerFirstLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerFirstLogin.StatusCode == HttpStatusCode.Accepted &&
           ownerFirstLoginBody.GetProperty("state").GetString() == "mfa_required",
        "MFA-ready Owner can be attached to the salon and must complete an MFA challenge to sign in.");
    var ownerFirstChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", mfaRequestToken!, new
        {
            code = recoveryCodes[1],
            useRecoveryCode = true
        });
    Ensure(ownerFirstChallenge.StatusCode == HttpStatusCode.OK &&
           (await mfaClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "Owner receives an application session only after membership provisioning and an MFA challenge.");

    var ownerLogout = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/logout", mfaRequestToken!, new { });
    Ensure(ownerLogout.StatusCode == HttpStatusCode.NoContent,
        "Authenticated owner can log out.");
    Ensure((await mfaClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Logout clears the authenticated application session.");

    var ownerMfaLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", mfaRequestToken!, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    var ownerMfaLoginBody = await ownerMfaLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerMfaLogin.StatusCode == HttpStatusCode.Accepted &&
           ownerMfaLoginBody.GetProperty("state").GetString() == "mfa_required",
        "Owner with MFA enabled receives only a two-factor challenge session.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId))
               .Count(type => type == "operational_auth.password_verified_mfa_pending") == 3,
        "Password verification that requires an MFA challenge is audited as pending.");

    var recoveryChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", mfaRequestToken!, new
        {
            code = recoveryCodes[0],
            useRecoveryCode = true
        });
    var recoveryChallengeBody = await recoveryChallenge.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(recoveryChallenge.StatusCode == HttpStatusCode.OK &&
           recoveryChallengeBody.GetProperty("state").GetString() == "authenticated",
        "A valid one-time recovery code completes the MFA challenge.");
    Ensure((await mfaClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "MFA authentication claims survive security-stamp refresh on the next request.");
    var recoveryAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(recoveryAudit.Contains("operational_mfa.recovery_code_redeemed"),
        "Successful one-time recovery-code use is durably audited.");

    var replayLogout = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/logout", mfaRequestToken!, new { });
    Ensure(replayLogout.StatusCode == HttpStatusCode.NoContent,
        "Owner can terminate the recovery-code-authenticated session.");

    var replayLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", mfaRequestToken!, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    Ensure(replayLogin.StatusCode == HttpStatusCode.Accepted,
        "Owner must complete MFA again after logout.");

    var replayChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", mfaRequestToken!, new
        {
            code = recoveryCodes[0],
            useRecoveryCode = true
        });
    Ensure(replayChallenge.StatusCode == HttpStatusCode.Unauthorized,
        "A previously redeemed recovery code cannot be reused.");
    var failedChallengeAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(failedChallengeAudit.Count(type => type == "operational_mfa.challenge_failed") == 1,
        "A failed MFA challenge is durably audited exactly once.");

    // Enrollment already consumed the current TOTP step. Wait for a new
    // RFC 6238 step so this challenge also proves replay protection is active.
    await Task.Delay(TimeSpan.FromSeconds(31));
    string secondTotpCode;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var owner = await users.FindByIdAsync(ownerUserId.ToString());
        Ensure(owner is not null, "Owner identity remains available for TOTP challenge verification.");
        secondTotpCode = await users.GenerateTwoFactorTokenAsync(
            owner!, users.Options.Tokens.AuthenticatorTokenProvider);
    }

    var totpChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", mfaRequestToken!, new
        {
            code = secondTotpCode,
            useRecoveryCode = false
        });
    Ensure(totpChallenge.StatusCode == HttpStatusCode.OK,
        "A fresh TOTP code completes the MFA challenge.");
    var completeMfaAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(completeMfaAudit.Contains("operational_mfa.totp_challenge_succeeded"),
        "Successful TOTP challenge is durably audited.");

    var backupOwnerEmail = "http-mfa-backup-owner-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    Guid backupOwnerId;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var backupOwner = new OperationalIdentityUser
        {
            UserName = backupOwnerEmail,
            Email = backupOwnerEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(backupOwner, ownerPassword);
        Ensure(created.Succeeded, "Backup Owner identity is created.");
        var enabled = await users.SetTwoFactorEnabledAsync(backupOwner, true);
        Ensure(enabled.Succeeded, "Backup Owner MFA is enabled before membership provisioning.");
        backupOwnerId = backupOwner.Id;
    }
    await SeedAdditionalMembershipAsync(migratorConnectionString, ownerTenantId, backupOwnerId, roleId: 1);

    await UpdateMembershipAsync(migratorConnectionString, ownerUserId, roleId: 3, isActive: true);
    var roleChangeAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(roleChangeAudit.Count(type => type == "tenant_membership.role_changed") == 1 &&
           !roleChangeAudit.Contains("tenant_membership.deactivated"),
        "A membership role change is audited by the database trigger exactly once, without a spurious activation event.");
    var demotedOwnerMe = await mfaClient.GetAsync("/api/v1/auth/me");
    var demotedOwnerBody = await demotedOwnerMe.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(demotedOwnerMe.StatusCode == HttpStatusCode.OK &&
           demotedOwnerBody.GetProperty("role").GetString() == TenantRole.Receptionist.ToString(),
        "A live Owner-to-Receptionist role change is reflected on the next request.");

    using var promotionClient = mfaFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var promotionEmail = "http-promotion-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string promotionPassword = "Atendee-Promotion-HTTP-2026!";
    var promotionTenantId = Guid.NewGuid();
    Guid promotionUserId;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var receptionist = new OperationalIdentityUser
        {
            UserName = promotionEmail,
            Email = promotionEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(receptionist, promotionPassword);
        Ensure(created.Succeeded, "Synthetic receptionist for role-change checks is created.");
        promotionUserId = receptionist.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, promotionTenantId, promotionUserId);

    var promotionAntiforgery = await promotionClient.GetAsync("/api/v1/auth/antiforgery");
    var promotionToken = (await promotionAntiforgery.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("requestToken").GetString();
    var promotionLogin = await PostWithCsrfAsync(promotionClient, "/api/v1/auth/login", promotionToken!, new
    {
        email = promotionEmail,
        password = promotionPassword
    });
    Ensure(promotionLogin.StatusCode == HttpStatusCode.OK &&
           (await promotionClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "Receptionist establishes a session before a server-side role change.");

    Ensure(await TryUpdateMembershipAsync(migratorConnectionString, promotionUserId, roleId: 1, isActive: true),
        "Database blocks promotion to Owner until the identity has completed MFA enrollment.");
    Ensure((await promotionClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "A blocked Owner promotion leaves the existing receptionist session unchanged.");

    await using (var scope = roleMatrixFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var receptionist = await users.FindByIdAsync(promotionUserId.ToString());
        Ensure(receptionist is not null, "Promotion test identity remains available.");
        var enabled = await users.SetTwoFactorEnabledAsync(receptionist!, true);
        Ensure(enabled.Succeeded, "Promotion test identity completes MFA before role elevation.");
    }
    await UpdateMembershipAsync(migratorConnectionString, promotionUserId, roleId: 1, isActive: true);
    Ensure((await promotionClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Forbidden,
        "A password-only session cannot use Owner privileges even after a server-side MFA-ready promotion.");

    using var deactivationClient = mfaFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var deactivationEmail = "http-deactivation-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string deactivationPassword = "Atendee-Deactivation-HTTP-2026!";
    var deactivationTenantId = Guid.NewGuid();
    Guid deactivationUserId;
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var receptionist = new OperationalIdentityUser
        {
            UserName = deactivationEmail,
            Email = deactivationEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(receptionist, deactivationPassword);
        Ensure(created.Succeeded, "Synthetic receptionist for membership-deactivation checks is created.");
        deactivationUserId = receptionist.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, deactivationTenantId, deactivationUserId);

    var deactivationAntiforgery = await deactivationClient.GetAsync("/api/v1/auth/antiforgery");
    var deactivationToken = (await deactivationAntiforgery.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("requestToken").GetString();
    var deactivationLogin = await PostWithCsrfAsync(
        deactivationClient, "/api/v1/auth/login", deactivationToken!, new
        {
            email = deactivationEmail,
            password = deactivationPassword
        });
    Ensure(deactivationLogin.StatusCode == HttpStatusCode.OK &&
           (await deactivationClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "Receptionist establishes a session before membership deactivation.");

    await UpdateMembershipAsync(migratorConnectionString, deactivationUserId, roleId: 3, isActive: false);
    var deactivationAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, deactivationUserId);
    Ensure(deactivationAudit.Count(type => type == "tenant_membership.deactivated") == 1 &&
           !deactivationAudit.Contains("tenant_membership.activated"),
        "A membership deactivation is audited by the database trigger exactly once.");
    Ensure((await deactivationClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Deactivating membership invalidates the existing tenant session on its next request.");

    // A fresh host isolates the role-matrix clients from the per-IP login budget
    // consumed by the preceding account/MFA scenarios.
    roleMatrixKeyRing = Path.Combine(Path.GetTempPath(), "atendee-role-matrix-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(roleMatrixKeyRing);
    roleMatrixFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = roleMatrixKeyRing
            });
        });
    });

    var managerEmail = "http-role-manager-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string managerPassword = "Atendee-Manager-Role-2026!";
    var managerTenantId = Guid.NewGuid();
    Guid managerUserId;
    await using (var scope = roleMatrixFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var manager = new OperationalIdentityUser
        {
            UserName = managerEmail,
            Email = managerEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(manager, managerPassword);
        Ensure(created.Succeeded, "Synthetic manager identity is created.");
        managerUserId = manager.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, managerTenantId, managerUserId, roleId: 2);

    using var managerClient = roleMatrixFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var managerAntiforgery = await managerClient.GetAsync("/api/v1/auth/antiforgery");
    var managerToken = (await managerAntiforgery.Content.ReadFromJsonAsync<JsonElement>())!
        .GetProperty("requestToken").GetString();
    var managerWrongPassword = await PostWithCsrfAsync(managerClient, "/api/v1/auth/login", managerToken!, new
    {
        email = managerEmail,
        password = "Incorrect-Manager-Password-2026!"
    });
    var managerFailureAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, managerUserId);
    Ensure(managerWrongPassword.StatusCode == HttpStatusCode.Unauthorized &&
           managerFailureAudit.Count(type => type == "operational_auth.login_failed") == 1 &&
           !managerFailureAudit.Contains("operational_auth.password_verified_mfa_pending"),
        "An incorrect password never produces a pending-MFA event; it is audited as a login failure.");

    var managerLogin = await PostWithCsrfAsync(managerClient, "/api/v1/auth/login", managerToken!, new
    {
        email = managerEmail,
        password = managerPassword
    });
    var managerLoginBody = await managerLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(managerLogin.StatusCode == HttpStatusCode.Accepted &&
           managerLoginBody.GetProperty("state").GetString() == "mfa_setup_required",
        "Manager without MFA receives only the restricted MFA-setup session.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, managerUserId))
               .Count(type => type == "operational_auth.password_verified_mfa_pending") == 1,
        "Manager password verification that requires MFA setup is audited as pending exactly once.");
    Ensure((await managerClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Manager cannot use the application API before completing MFA.");

    var professionalEmail = "http-role-professional-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string professionalPassword = "Atendee-Professional-Role-2026!";
    var professionalTenantId = Guid.NewGuid();
    Guid professionalUserId;
    await using (var scope = roleMatrixFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var professional = new OperationalIdentityUser
        {
            UserName = professionalEmail,
            Email = professionalEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(professional, professionalPassword);
        Ensure(created.Succeeded, "Synthetic professional identity is created.");
        professionalUserId = professional.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, professionalTenantId, professionalUserId, roleId: 4);

    using var professionalClient = roleMatrixFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var professionalAntiforgery = await professionalClient.GetAsync("/api/v1/auth/antiforgery");
    var professionalToken = (await professionalAntiforgery.Content.ReadFromJsonAsync<JsonElement>())!
        .GetProperty("requestToken").GetString();
    var professionalLogin = await PostWithCsrfAsync(
        professionalClient, "/api/v1/auth/login", professionalToken!, new
        {
            email = professionalEmail,
            password = professionalPassword
        });
    Ensure(professionalLogin.StatusCode == HttpStatusCode.OK,
        "Professional can authenticate without the privileged-role MFA setup gate.");
    var professionalMe = await professionalClient.GetAsync("/api/v1/auth/me");
    var professionalMeBody = await professionalMe.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(professionalMe.StatusCode == HttpStatusCode.OK &&
           professionalMeBody.GetProperty("role").GetString() == TenantRole.Professional.ToString(),
        "Professional session resolves its current role from server-side membership.");

    // Tenant lifecycle policy through real HTTP sessions. A fresh host gives these
    // logins their own per-IP login budget (4 of 5 permits are used here).
    statusKeyRing = Path.Combine(Path.GetTempPath(), "atendee-status-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(statusKeyRing);
    statusFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = statusKeyRing
            });
        });
    });

    const string statusPassword = "Atendee-Status-Policy-2026!";

    async Task<(Guid TenantId, Guid UserId, string Email)> SeedStatusReceptionistAsync(int statusId)
    {
        var memberEmail = "http-status-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var memberTenantId = Guid.NewGuid();
        Guid memberUserId;
        await using (var scope = statusFactory!.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
            var member = new OperationalIdentityUser
            {
                UserName = memberEmail,
                Email = memberEmail,
                EmailConfirmed = true
            };
            var created = await users.CreateAsync(member, statusPassword);
            Ensure(created.Succeeded, "Synthetic status-policy identity is created.");
            memberUserId = member.Id;
        }

        await SeedMembershipAsync(migratorConnectionString, memberTenantId, memberUserId,
            roleId: 3, statusId: statusId);
        return (memberTenantId, memberUserId, memberEmail);
    }

    async Task<(HttpClient Client, HttpResponseMessage Login)> LoginStatusMemberAsync(string memberEmail)
    {
        var memberClient = statusFactory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        var memberAntiforgery = await memberClient.GetAsync("/api/v1/auth/antiforgery");
        var memberToken = (await memberAntiforgery.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requestToken").GetString();
        var memberLogin = await PostWithCsrfAsync(memberClient, "/api/v1/auth/login", memberToken!, new
        {
            email = memberEmail,
            password = statusPassword
        });
        return (memberClient, memberLogin);
    }

    // Suspended (5): read-only members may sign in; a later move to Closed (7) ends the session.
    var suspendedMember = await SeedStatusReceptionistAsync(statusId: 5);
    var (suspendedClient, suspendedLogin) = await LoginStatusMemberAsync(suspendedMember.Email);
    Ensure(suspendedLogin.StatusCode == HttpStatusCode.OK,
        "A receptionist of a Suspended tenant can still sign in (read-only, pay and export policy).");
    var suspendedMe = await suspendedClient.GetAsync("/api/v1/auth/me");
    var suspendedMeBody = await suspendedMe.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(suspendedMe.StatusCode == HttpStatusCode.OK &&
           suspendedMeBody.GetProperty("tenantStatus").GetString() == TenantStatus.Suspended.ToString(),
        "The server-resolved tenant status is reported for a Suspended tenant.");
    await UpdateTenantStatusAsync(migratorConnectionString, suspendedMember.TenantId, statusId: 7);
    var suspendedTenantAudit = await ReadTenantStatusAuditAsync(migratorConnectionString, suspendedMember.TenantId);
    Ensure(suspendedTenantAudit.Length == 1 &&
           suspendedTenantAudit[0].From == 5 && suspendedTenantAudit[0].To == 7 &&
           !suspendedTenantAudit[0].HasActor && !suspendedTenantAudit[0].HasIdentityTarget,
        "A tenant status change is audited by the database trigger exactly once, with from/to status and no actor or identity target.");
    Ensure((await suspendedClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Forbidden,
        "Closing a tenant (status Closed) ends an already-issued session on its next request.");
    suspendedClient.Dispose();

    // Closing (6): non-owner members are denied at login and the denial is audited as a login failure.
    var closingMember = await SeedStatusReceptionistAsync(statusId: 6);
    var (closingClient, closingLogin) = await LoginStatusMemberAsync(closingMember.Email);
    var closingAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, closingMember.UserId);
    Ensure(closingLogin.StatusCode == HttpStatusCode.Unauthorized &&
           closingAudit.Count(type => type == "operational_auth.login_failed") == 1 &&
           !closingAudit.Contains("operational_auth.login_succeeded"),
        "A non-owner member of a Closing tenant cannot sign in and the denial is audited.");
    Ensure((await closingClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "A denied Closing-tenant login leaves no usable session.");
    closingClient.Dispose();

    // SetupRequired (1): only Owner and Manager configure the salon; a receptionist is denied.
    var setupMember = await SeedStatusReceptionistAsync(statusId: 1);
    var (setupClient, setupLogin) = await LoginStatusMemberAsync(setupMember.Email);
    Ensure(setupLogin.StatusCode == HttpStatusCode.Unauthorized,
        "A receptionist cannot sign in while the tenant is SetupRequired.");
    setupClient.Dispose();

    // Active (3) to Closing (6) mid-session: a non-owner loses access on the next request.
    var liveMember = await SeedStatusReceptionistAsync(statusId: 3);
    var (liveClient, liveLogin) = await LoginStatusMemberAsync(liveMember.Email);
    Ensure(liveLogin.StatusCode == HttpStatusCode.OK &&
           (await liveClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "A receptionist of an Active tenant has a working session before the status change.");
    await UpdateTenantStatusAsync(migratorConnectionString, liveMember.TenantId, statusId: 6);
    var liveTenantAudit = await ReadTenantStatusAuditAsync(migratorConnectionString, liveMember.TenantId);
    Ensure(liveTenantAudit.Length == 1 && liveTenantAudit[0].From == 3 && liveTenantAudit[0].To == 6,
        "Moving a tenant from Active to Closing is audited with the exact from/to status.");
    await UpdateTenantStatusAsync(migratorConnectionString, liveMember.TenantId, statusId: 6);
    Ensure((await ReadTenantStatusAuditAsync(migratorConnectionString, liveMember.TenantId)).Length == 1,
        "A tenant update that leaves the status unchanged is not audited.");
    Ensure((await liveClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Forbidden,
        "Moving a tenant to Closing revokes a non-owner member's live session on the next request.");
    liveClient.Dispose();

    Console.WriteLine("RESULT: HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks passed.");
}
finally
{
    factory.Dispose();
    mfaFactory?.Dispose();
    roleMatrixFactory?.Dispose();
    statusFactory?.Dispose();
    try
    {
        if (statusKeyRing is not null && Directory.Exists(statusKeyRing))
            Directory.Delete(statusKeyRing, recursive: true);
        if (mfaKeyRing is not null && Directory.Exists(mfaKeyRing))
            Directory.Delete(mfaKeyRing, recursive: true);
        if (roleMatrixKeyRing is not null && Directory.Exists(roleMatrixKeyRing))
            Directory.Delete(roleMatrixKeyRing, recursive: true);
        if (Directory.Exists(keyRing))
            Directory.Delete(keyRing, recursive: true);
    }
    catch
    {
        // Temporary test key cleanup must not hide the security-test result.
    }
}

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atendee.sln")))
        directory = directory.Parent;

    return directory?.FullName
        ?? throw new DirectoryNotFoundException("Could not locate Atendee.sln from " + startPath);
}

static string Required(string name)
    => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(name + " is required; HTTP integration checks cannot be skipped.");

static void Ensure(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException("FAIL: " + description);

    Console.WriteLine("PASS " + description);
}

static bool HasCookieAttributes(string cookies, string cookieName)
{
    var line = cookies.Split(',', StringSplitOptions.TrimEntries)
        .FirstOrDefault(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal));
    if (line is null)
        return false;

    return line.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("path=/", StringComparison.OrdinalIgnoreCase);
}

static async Task<HttpResponseMessage> PostWithCsrfAsync(
    HttpClient client,
    string path,
    string token,
    object body)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = JsonContent.Create(body)
    };
    request.Headers.Add("X-CSRF-TOKEN", token);
    return await client.SendAsync(request);
}

static async Task UpdateTenantStatusAsync(string connectionString, Guid tenantId, int statusId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using (var update = new NpgsqlCommand(
        "UPDATE atendee.tenants SET status = @status WHERE id = @id", connection, transaction))
    {
        update.Parameters.AddWithValue("status", (short)statusId);
        update.Parameters.AddWithValue("id", tenantId);
        var affected = await update.ExecuteNonQueryAsync();
        Ensure(affected == 1, "Synthetic tenant status update affects exactly one row.");
    }

    await transaction.CommitAsync();
}

static async Task SeedMembershipAsync(
    string connectionString, Guid tenantId, Guid userId, int roleId = 3, int statusId = 3)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    Guid? fixtureOwnerId = null;
    if (roleId != 1)
    {
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
            await role.ExecuteNonQueryAsync();
        fixtureOwnerId = Guid.NewGuid();
        await using (var fixtureOwner = new NpgsqlCommand(
            """
            INSERT INTO identity."AspNetUsers"
                ("Id","UserName","NormalizedUserName","Email","NormalizedEmail",
                 "EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
            VALUES(@id,'http-owner-' || @id::text,upper('http-owner-' || @id::text),
                   'http-owner-' || @id::text || '@example.invalid',
                   upper('http-owner-' || @id::text || '@example.invalid'),
                   true,false,true,true,0)
            """, connection, transaction))
        {
            fixtureOwner.Parameters.AddWithValue("id", fixtureOwnerId.Value);
            await fixtureOwner.ExecuteNonQueryAsync();
        }
    }

    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using (var tenant = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenants(id,name,slug,time_zone_id,status,created_at_utc)
        VALUES(@id,'HTTP Synthetic Salon','http-' || @id::text,'America/Sao_Paulo',@status,now())
        """, connection, transaction))
    {
        tenant.Parameters.AddWithValue("id", tenantId);
        tenant.Parameters.AddWithValue("status", (short)statusId);
        await tenant.ExecuteNonQueryAsync();
    }

    if (fixtureOwnerId is not null)
    {
        await using var ownerMembership = new NpgsqlCommand(
            """
            INSERT INTO atendee.tenant_users
                (tenant_id,identity_user_id,display_name,role,is_active,created_at_utc)
            VALUES(@tenant,@owner,'Synthetic MFA Owner',1,true,now())
            """, connection, transaction);
        ownerMembership.Parameters.AddWithValue("tenant", tenantId);
        ownerMembership.Parameters.AddWithValue("owner", fixtureOwnerId.Value);
        await ownerMembership.ExecuteNonQueryAsync();
    }

    await using (var membership = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenant_users
            (tenant_id,identity_user_id,display_name,role,is_active,created_at_utc)
        VALUES(@tenant,@user,'HTTP Synthetic User',@role,true,now())
        """, connection, transaction))
    {
        membership.Parameters.AddWithValue("tenant", tenantId);
        membership.Parameters.AddWithValue("user", userId);
        membership.Parameters.AddWithValue("role", roleId);
        await membership.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task SeedAdditionalMembershipAsync(
    string connectionString,
    Guid tenantId,
    Guid userId,
    int roleId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var membership = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenant_users
            (tenant_id,identity_user_id,display_name,role,is_active,created_at_utc)
        VALUES(@tenant,@user,'HTTP Synthetic Audit Target',@role,true,now())
        """, connection, transaction);
    membership.Parameters.AddWithValue("tenant", tenantId);
    membership.Parameters.AddWithValue("user", userId);
    membership.Parameters.AddWithValue("role", roleId);
    await membership.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
}

static async Task<bool> TryUpdateMembershipAsync(
    string connectionString, Guid userId, int roleId, bool isActive)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();
    try
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE atendee.tenant_users
            SET role = @role, is_active = @active
            WHERE identity_user_id = @user
            """, connection, transaction);
        update.Parameters.AddWithValue("role", roleId);
        update.Parameters.AddWithValue("active", isActive);
        update.Parameters.AddWithValue("user", userId);
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return false;
    }
    catch (PostgresException ex) when (ex.SqlState == "23514")
    {
        return true;
    }
}

static async Task UpdateMembershipAsync(
    string connectionString,
    Guid userId,
    int roleId,
    bool isActive)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using (var update = new NpgsqlCommand(
        """
        UPDATE atendee.tenant_users
        SET role = @role, is_active = @active
        WHERE identity_user_id = @user
        """, connection, transaction))
    {
        update.Parameters.AddWithValue("role", roleId);
        update.Parameters.AddWithValue("active", isActive);
        update.Parameters.AddWithValue("user", userId);
        var affected = await update.ExecuteNonQueryAsync();
        Ensure(affected == 1, "Synthetic membership update affects exactly one row.");
    }

    await transaction.CommitAsync();
}

static async Task<(string EventType, Guid? ActorId, Guid TargetId, Guid? TargetTenantId)[]> ReadMembershipRoleChangeAuditAsync(
    string connectionString,
    Guid targetUserId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT ""EventType"", ""ActorIdentityUserId"", ""TargetIdentityUserId"", ""TargetTenantId""
          FROM identity.""SecurityAuditEvents""
          WHERE ""TargetIdentityUserId"" = @target AND ""EventType"" = 'tenant_membership.role_changed'
          ORDER BY ""OccurredAtUtc"", ""Id""", connection, transaction);
    command.Parameters.AddWithValue("target", targetUserId);
    var values = new List<(string EventType, Guid? ActorId, Guid TargetId, Guid? TargetTenantId)>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            values.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
    }

    await transaction.CommitAsync();
    return values.ToArray();
}

static async Task<string[]> ReadSecurityAuditTypesAsync(string connectionString, Guid targetUserId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT ""EventType"" FROM identity.""SecurityAuditEvents"" WHERE ""TargetIdentityUserId"" = @target ORDER BY ""OccurredAtUtc"", ""Id"" ", connection, transaction);
    command.Parameters.AddWithValue("target", targetUserId);
    var values = new List<string>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
    }

    await transaction.CommitAsync();
    return values.ToArray();
}

static async Task<(short From, short To, bool HasActor, bool HasIdentityTarget)[]> ReadTenantStatusAuditAsync(
    string connectionString,
    Guid tenantId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT ""FromTenantStatus"", ""ToTenantStatus"", ""ActorIdentityUserId"" IS NOT NULL, ""TargetIdentityUserId"" IS NOT NULL FROM identity.""SecurityAuditEvents"" WHERE ""TargetTenantId"" = @tenant AND ""EventType"" = 'tenant_status.changed' ORDER BY ""OccurredAtUtc"", ""Id"" ", connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    var values = new List<(short From, short To, bool HasActor, bool HasIdentityTarget)>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            values.Add((reader.GetInt16(0), reader.GetInt16(1), reader.GetBoolean(2), reader.GetBoolean(3)));
    }

    await transaction.CommitAsync();
    return values.ToArray();
}
static async Task<(string EventType, Guid? TargetTenantId)[]> ReadAuditTenantTargetsAsync(
    string connectionString,
    Guid targetUserId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT ""EventType"", ""TargetTenantId""
          FROM identity.""SecurityAuditEvents""
          WHERE ""TargetIdentityUserId"" = @target
          ORDER BY ""OccurredAtUtc"", ""Id""", connection, transaction);
    command.Parameters.AddWithValue("target", targetUserId);
    var values = new List<(string EventType, Guid? TargetTenantId)>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            values.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1)));
    }

    await transaction.CommitAsync();
    return values.ToArray();
}

static async Task<Guid> InsertAuditEventWithForgedTenantAsync(
    string connectionString,
    Guid targetUserId,
    Guid forgedTenantId)
{
    var eventId = Guid.NewGuid();
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId",
             "OccurredAtUtc", "CorrelationId", "TargetTenantId")
        VALUES (@id, 'operational_auth.login_failed', NULL, @target, now(), NULL, @tenant)
        """, connection);
    command.Parameters.AddWithValue("id", eventId);
    command.Parameters.AddWithValue("target", targetUserId);
    command.Parameters.AddWithValue("tenant", forgedTenantId);
    await command.ExecuteNonQueryAsync();
    return eventId;
}

static async Task<bool> AuthLoginCannotReadTenantUsersAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    try
    {
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id FROM atendee.tenant_users LIMIT 1", connection);
        await command.ExecuteScalarAsync();
        return false;
    }
    catch (PostgresException exception) when
        (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
    {
        return true;
    }
}

static async Task<bool> AuthLoginCannotExecuteAuditTargetResolverAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    try
    {
        await using var command = new NpgsqlCommand(
            "SELECT atendee.resolve_audit_target_tenant()", connection);
        await command.ExecuteScalarAsync();
        return false;
    }
    catch (PostgresException exception) when
        (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
    {
        return true;
    }
}

static async Task<int> CountAuditEventsWithActorAsync(
    string connectionString,
    Guid targetUserId,
    params string[] eventTypes)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT count(*) FROM identity.""SecurityAuditEvents"" WHERE ""TargetIdentityUserId"" = @target AND ""EventType"" = ANY(@types) AND ""ActorIdentityUserId"" IS NOT NULL", connection, transaction);
    command.Parameters.AddWithValue("target", targetUserId);
    command.Parameters.AddWithValue("types", eventTypes);
    var count = Convert.ToInt32(await command.ExecuteScalarAsync());
    await transaction.CommitAsync();
    return count;
}
