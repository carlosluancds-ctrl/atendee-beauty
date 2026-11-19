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
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? promotionFactory = null;
string? promotionKeyRing = null;
string? mfaKeyRing = null;
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? roleMatrixFactory = null;
string? roleMatrixKeyRing = null;
WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>? statusFactory = null;
string? statusKeyRing = null;
string? lockoutKeyRing = null;
string? sequentialLockoutKeyRing = null;
string? crossFlowKeyRing = null;
string? revocationKeyRing = null;

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

    Ensure((await client.GetAsync("/api/v1/tenant")).StatusCode == HttpStatusCode.Unauthorized,
        "Tenant profile endpoint rejects anonymous requests.");

    var activationEmail = "activation-http-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    Guid activationUserId;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var pending = new OperationalIdentityUser
        {
            UserName = activationEmail,
            Email = activationEmail,
            EmailConfirmed = false,
            TwoFactorEnabled = false,
            LockoutEnabled = true
        };
        var created = await users.CreateAsync(pending);
        Ensure(created.Succeeded && pending.PasswordHash is null,
            "A pending activation identity is created without a password or MFA.");
        activationUserId = pending.Id;
    }

    var activationToken = ActivationTokenCodec.CreateToken();
    var activationHash = ActivationTokenCodec.HashToken(activationToken);
    await using (var activationConnection = new NpgsqlConnection(migratorConnectionString))
    {
        await activationConnection.OpenAsync();
        await using var activationTransaction = await activationConnection.BeginTransactionAsync();
        await using (var role = new NpgsqlCommand(
            "SET LOCAL ROLE atendee_identity_owner", activationConnection, activationTransaction))
            await role.ExecuteNonQueryAsync();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO identity."OperationalIdentityActivationTokens"
                ("UserId", "TokenHash", "CreatedAtUtc", "ExpiresAtUtc", "ConsumedAtUtc")
            VALUES (@user_id, @token_hash, @created_at, @expires_at, NULL)
            """, activationConnection, activationTransaction))
        {
            var issuedAt = DateTimeOffset.UtcNow;
            insert.Parameters.AddWithValue("user_id", activationUserId);
            insert.Parameters.AddWithValue("token_hash", activationHash);
            insert.Parameters.AddWithValue("created_at", issuedAt);
            insert.Parameters.AddWithValue("expires_at", issuedAt.AddMinutes(30));
            await insert.ExecuteNonQueryAsync();
        }
        await activationTransaction.CommitAsync();
    }

    var rejectedActivationPassword = await PostWithCsrfAsync(
        client, "/api/v1/auth/activate", requestToken!, new
        {
            token = activationToken,
            password = "short"
        });
    Ensure(rejectedActivationPassword.StatusCode == HttpStatusCode.BadRequest,
        "Activation rejects a password that does not meet Identity policy without consuming the token.");

    var activated = await PostWithCsrfAsync(client, "/api/v1/auth/activate", requestToken!, new
    {
        token = activationToken,
        password = "Atendee-Activation-HTTP-2026!"
    });
    Ensure(activated.StatusCode == HttpStatusCode.OK,
        "A valid one-time activation token confirms the email and sets the password.");
    var activationBody = await activated.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(activationBody.GetProperty("state").GetString() == "activated_sign_in_required",
        "Activation explicitly requires a separate sign-in.");
    var activationCookies = activated.Headers.TryGetValues("Set-Cookie", out var activationSetCookies)
        ? string.Join("; ", activationSetCookies)
        : string.Empty;
    Ensure(!activationCookies.Contains("__Host-Atendee.Auth", StringComparison.Ordinal),
        "Activation does not issue an authenticated application session.");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var activatedUser = await users.FindByIdAsync(activationUserId.ToString());
        Ensure(activatedUser is not null && activatedUser.EmailConfirmed &&
               activatedUser.PasswordHash is not null && !activatedUser.TwoFactorEnabled,
            "Activation confirms email and sets a password while leaving MFA enrollment pending.");
        Ensure(await users.CheckPasswordAsync(activatedUser!, "Atendee-Activation-HTTP-2026!"),
            "The activated password is stored through the configured ASP.NET Identity hasher.");
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantMembershipResolver>();
        Ensure(await resolver.ResolveAsync(activationUserId, CancellationToken.None) is null,
            "Activation does not create a salon membership.");
    }

    var replayedActivation = await PostWithCsrfAsync(client, "/api/v1/auth/activate", requestToken!, new
    {
        token = activationToken,
        password = "Atendee-Activation-HTTP-2026!"
    });
    Ensure(replayedActivation.StatusCode == HttpStatusCode.BadRequest &&
           (await replayedActivation.Content.ReadAsStringAsync()).Contains(
               "activation_invalid_or_expired", StringComparison.Ordinal),
        "A consumed activation token is rejected with the generic invalid-or-expired response.");


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

    var tenantProfile = await client.GetAsync("/api/v1/tenant");
    Ensure(tenantProfile.StatusCode == HttpStatusCode.OK,
        "Authenticated user can read the current tenant profile.");
    var tenantProfileBody = await tenantProfile.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(tenantProfileBody.GetProperty("tenantId").GetString() == tenantId.ToString() &&
           tenantProfileBody.GetProperty("name").GetString() == "HTTP Synthetic Salon" &&
           tenantProfileBody.GetProperty("slug").GetString() == "http-" + tenantId.ToString() &&
           tenantProfileBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
           tenantProfileBody.GetProperty("status").GetString() == TenantStatus.Active.ToString(),
        "Tenant profile is returned from the server-resolved membership and persisted tenant row.");
    Ensure(tenantProfileBody.GetProperty("membership").GetProperty("role").GetString() ==
           TenantRole.Receptionist.ToString(),
        "Tenant profile includes only the caller's persisted membership role.");

    var forgedRequestTenantId = Guid.NewGuid();
    using (var forgedContextRequest = new HttpRequestMessage(
        HttpMethod.Get,
        $"/api/v1/tenant?tenantId={forgedRequestTenantId:D}&actorId={spoofedActorId:D}&role=Owner&status=PendingVerification"))
    {
        forgedContextRequest.Headers.Add("X-Tenant-Id", forgedRequestTenantId.ToString("D"));
        forgedContextRequest.Headers.Add("X-Actor-Id", spoofedActorId.ToString("D"));
        forgedContextRequest.Headers.Add("X-Tenant-Role", "Owner");
        forgedContextRequest.Headers.Add("X-Tenant-Status", "PendingVerification");
        using var forgedContextResponse = await client.SendAsync(forgedContextRequest);
        var forgedContextBody = await forgedContextResponse.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(forgedContextResponse.StatusCode == HttpStatusCode.OK &&
               forgedContextBody.GetProperty("tenantId").GetString() == tenantId.ToString() &&
               forgedContextBody.GetProperty("status").GetString() == TenantStatus.Active.ToString() &&
               forgedContextBody.GetProperty("membership").GetProperty("role").GetString() == TenantRole.Receptionist.ToString(),
            "Forged tenant, actor, role and status headers/query parameters cannot override persisted request context.");
    }

    var receptionistServices = await client.GetAsync("/api/v1/services");
    Ensure(receptionistServices.StatusCode == HttpStatusCode.OK,
        "Receptionist can read the tenant service catalogue when tenant status allows ReadData.");
    var receptionistRequestToken = await GetAntiforgeryRequestTokenAsync(client);
    using (var forgedOwnerOnboarding = new HttpRequestMessage(
        HttpMethod.Post, "/api/v1/tenant/onboarding/complete"))
    {
        forgedOwnerOnboarding.Headers.Add("X-CSRF-TOKEN", receptionistRequestToken);
        forgedOwnerOnboarding.Headers.Add("X-Tenant-Id", forgedRequestTenantId.ToString("D"));
        forgedOwnerOnboarding.Headers.Add("X-Actor-Id", spoofedActorId.ToString("D"));
        forgedOwnerOnboarding.Headers.Add("X-Tenant-Role", "Owner");
        forgedOwnerOnboarding.Headers.Add("X-Tenant-Status", "PendingVerification");
        forgedOwnerOnboarding.Content = JsonContent.Create(new
        {
            tenantId = forgedRequestTenantId,
            actorId = spoofedActorId,
            role = "Owner",
            status = "PendingVerification"
        });
        using var forgedOwnerOnboardingResponse = await client.SendAsync(forgedOwnerOnboarding);
        Ensure(forgedOwnerOnboardingResponse.StatusCode == HttpStatusCode.Forbidden,
            "A receptionist cannot escalate to Owner or complete onboarding through forged tenant/actor/role headers or body fields.");
    }

    var receptionistCreate = await PostWithCsrfAsync(client, "/api/v1/services", receptionistRequestToken, new
    {
        name = "Unauthorized service",
        durationMinutes = 30,
        priceMinorUnits = 5000L,
        currency = "BRL",
        bufferBeforeMinutes = 0,
        bufferAfterMinutes = 0,
        isActive = true
    });
    Ensure(receptionistCreate.StatusCode == HttpStatusCode.Forbidden,
        "Receptionist cannot create or manage salon services.");

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
    Ensure(await AuthLoginCannotReadTenantsAsync(identityConnectionString),
        "The authentication database role still has no direct SELECT access to tenant profiles.");
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
    var enrollmentStartCookies = enrollmentStart.Headers.TryGetValues("Set-Cookie", out var enrollmentCookieValues)
        ? enrollmentCookieValues.ToArray()
        : Array.Empty<string>();
    Ensure(enrollmentStart.StatusCode == HttpStatusCode.OK &&
           enrollmentStartCookies.Any(value => value.StartsWith("__Host-Atendee.MfaSetup=", StringComparison.Ordinal)),
        "Owner can start authenticator enrollment and receives a setup ticket bound to the rotated security stamp.");
    var enrollmentRequestToken = await GetAntiforgeryRequestTokenAsync(mfaClient);
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
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", enrollmentRequestToken, new { code = " " });
    Ensure(blankEnrollment.StatusCode == HttpStatusCode.BadRequest &&
           (await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId)).Length ==
               auditBeforeInvalidEnrollment.Length,
        "Malformed enrollment input is rejected without growing the audit ledger.");

    var wrongEnrollmentCode = enrollmentCode == "000000" ? "000001" : "000000";
    var invalidEnrollment = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", enrollmentRequestToken, new
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
        mfaClient, "/api/v1/auth/mfa/enrollment/verify", enrollmentRequestToken, new
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

    var postEnrollmentToken = await GetAntiforgeryRequestTokenAsync(mfaClient);
    var ownerMeBeforeMembership = await mfaClient.GetAsync("/api/v1/auth/me");
    Ensure(ownerMeBeforeMembership.StatusCode == HttpStatusCode.Unauthorized,
        "Pre-membership MFA enrollment does not issue an application session.");

    await SeedMembershipAsync(migratorConnectionString, ownerTenantId, ownerUserId, roleId: 1, statusId: 0);
    var ownerFirstLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", postEnrollmentToken, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    var ownerFirstLoginBody = await ownerFirstLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerFirstLogin.StatusCode == HttpStatusCode.Accepted &&
           ownerFirstLoginBody.GetProperty("state").GetString() == "mfa_required",
        "MFA-ready Owner can be attached to the salon and must complete an MFA challenge to sign in.");
    var ownerPostLoginToken = await GetAntiforgeryRequestTokenAsync(mfaClient);
    var ownerFirstChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", ownerPostLoginToken, new
        {
            code = recoveryCodes[1],
            useRecoveryCode = true
        });
    Ensure(ownerFirstChallenge.StatusCode == HttpStatusCode.OK &&
           (await mfaClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "Owner receives an application session only after membership provisioning and an MFA challenge.");
    var ownerAuthenticatedToken = await GetAntiforgeryRequestTokenAsync(mfaClient);

    var pendingTenantProfile = await mfaClient.GetAsync("/api/v1/tenant");
    var pendingTenantProfileBody = await pendingTenantProfile.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(pendingTenantProfile.StatusCode == HttpStatusCode.OK &&
           pendingTenantProfileBody.GetProperty("status").GetString() == TenantStatus.PendingVerification.ToString() &&
           pendingTenantProfileBody.GetProperty("membership").GetProperty("role").GetString() == TenantRole.Owner.ToString(),
        "The authenticated Owner can read the current salon profile resolved from server-side membership.");

    var completedOnboarding = await PostWithCsrfAsync(
        mfaClient, "/api/v1/tenant/onboarding/complete", ownerAuthenticatedToken, new { });
    var completedOnboardingBody = await completedOnboarding.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(completedOnboarding.StatusCode == HttpStatusCode.OK &&
           completedOnboardingBody.GetProperty("status").GetString() == TenantStatus.SetupRequired.ToString() &&
           completedOnboardingBody.GetProperty("onboardingCompleted").GetBoolean(),
        "An email-confirmed, MFA-authenticated active Owner moves the salon from PendingVerification to SetupRequired.");

    var onboardingAudit = await ReadTenantStatusAuditAsync(migratorConnectionString, ownerTenantId);
    Ensure(onboardingAudit.Length == 1 && onboardingAudit[0].From == 0 && onboardingAudit[0].To == 1 &&
           onboardingAudit[0].ActorId == ownerUserId && !onboardingAudit[0].HasIdentityTarget,
        "The onboarding status audit records the authenticated Owner as its trusted actor and targets only the tenant.");

    var repeatedOnboarding = await PostWithCsrfAsync(
        mfaClient, "/api/v1/tenant/onboarding/complete", ownerAuthenticatedToken, new { });
    Ensure(repeatedOnboarding.StatusCode == HttpStatusCode.OK &&
           (await ReadTenantStatusAuditAsync(migratorConnectionString, ownerTenantId)).Length == 1,
        "Repeating onboarding completion is idempotent and does not create a second status audit event.");

    // The remaining service/professional integration checks share this tenant
    // fixture and require an operational status. Reset through the migrator only
    // after the PendingVerification -> SetupRequired behavior has been asserted.
    await UpdateTenantStatusAsync(migratorConnectionString, ownerTenantId, statusId: 3);

    var createSalonService = await PostWithCsrfAsync(mfaClient, "/api/v1/services", ownerAuthenticatedToken, new
    {
        name = "Corte de cabelo",
        durationMinutes = 45,
        priceMinorUnits = 7500L,
        currency = "BRL",
        bufferBeforeMinutes = 5,
        bufferAfterMinutes = 10,
        isActive = true
    });
    Ensure(createSalonService.StatusCode == HttpStatusCode.Created,
        "Owner can create a tenant-scoped service with explicit duration, minor-unit price, currency and buffers.");
    var createdSalonService = await createSalonService.Content.ReadFromJsonAsync<JsonElement>();
    var salonServiceId = createdSalonService.GetProperty("id").GetGuid();
    Ensure(createdSalonService.GetProperty("priceMinorUnits").GetInt64() == 7500 &&
           createdSalonService.GetProperty("currency").GetString() == "BRL" &&
           createdSalonService.GetProperty("durationMinutes").GetInt32() == 45,
        "Service API preserves integer money, explicit ISO currency and duration.");
    var ownerServiceList = await mfaClient.GetAsync("/api/v1/services");
    Ensure(ownerServiceList.StatusCode == HttpStatusCode.OK,
        "Owner can list services belonging to the current salon.");
    var ownerServicesJson = await ownerServiceList.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerServicesJson.GetArrayLength() == 1 &&
           ownerServicesJson[0].GetProperty("id").GetGuid() == salonServiceId,
        "Tenant service list contains the created service.");
    Ensure(await ServiceIsHiddenFromTenantAsync(tenantConnectionString, tenantId, ownerTenantId, salonServiceId),
        "PostgreSQL RLS hides a service from a different salon even when its UUID is known.");
    var assignedAssignedProfessionalEmail = "http-professional-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string assignedAssignedProfessionalPassword = "Atendee-Professional-HTTP-2026!";
    var assignedAssignedProfessionalUserId = Guid.NewGuid();
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var professionalUser = new OperationalIdentityUser
        {
            Id = assignedAssignedProfessionalUserId,
            UserName = assignedAssignedProfessionalEmail,
            Email = assignedAssignedProfessionalEmail,
            EmailConfirmed = true,
            TwoFactorEnabled = false
        };
        var created = await users.CreateAsync(professionalUser, assignedAssignedProfessionalPassword);
        Ensure(created.Succeeded, "Synthetic professional identity is created for service-assignment tests.");
    }
    await SeedAdditionalMembershipAsync(migratorConnectionString, ownerTenantId, assignedAssignedProfessionalUserId, roleId: 4);

    var eligibleProfessionalsBeforeCreate = await mfaClient.GetAsync("/api/v1/professionals/eligible-members");
    Ensure(eligibleProfessionalsBeforeCreate.StatusCode == HttpStatusCode.OK,
        "Owner can list only eligible Professional memberships in the current salon.");
    var eligibleProfessionalsBody = await eligibleProfessionalsBeforeCreate.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(eligibleProfessionalsBody.GetArrayLength() == 1 &&
           eligibleProfessionalsBody[0].GetProperty("identityUserId").GetGuid() == assignedAssignedProfessionalUserId,
        "Eligible-professional discovery returns the active Professional membership without unrelated identities.");

    var assignedInvalidProfessional = await PostWithCsrfAsync(mfaClient, "/api/v1/professionals", ownerAuthenticatedToken, new
    {
        identityUserId = ownerUserId
    });
    Ensure(assignedInvalidProfessional.StatusCode == HttpStatusCode.BadRequest,
        "Owner cannot create a professional profile for an identity with the wrong tenant role.");

    var assignedCreateProfessional = await PostWithCsrfAsync(mfaClient, "/api/v1/professionals", ownerAuthenticatedToken, new
    {
        identityUserId = assignedAssignedProfessionalUserId
    });
    Ensure(assignedCreateProfessional.StatusCode == HttpStatusCode.Created,
        "Owner can create a professional profile only for an active Professional membership.");
    var assignedAssignedProfessionalProfile = await assignedCreateProfessional.Content.ReadFromJsonAsync<JsonElement>();
    var assignedAssignedProfessionalProfileId = assignedAssignedProfessionalProfile.GetProperty("id").GetGuid();
    Ensure(assignedAssignedProfessionalProfile.GetProperty("identityUserId").GetGuid() == assignedAssignedProfessionalUserId &&
           !string.IsNullOrWhiteSpace(assignedAssignedProfessionalProfile.GetProperty("displayName").GetString()),
        "Professional profile binds the operational identity and uses the trusted membership display name.");

    var eligibleProfessionalsAfterCreate = await mfaClient.GetAsync("/api/v1/professionals/eligible-members");
    var eligibleProfessionalsAfterCreateBody = await eligibleProfessionalsAfterCreate.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(eligibleProfessionalsAfterCreate.StatusCode == HttpStatusCode.OK &&
           eligibleProfessionalsAfterCreateBody.GetArrayLength() == 0,
        "A membership disappears from eligible-professional discovery after profile creation.");

    var assignedInvalidAssignment = await PutWithCsrfAsync(
        mfaClient, $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/services",
        ownerAuthenticatedToken, new { serviceIds = new[] { Guid.NewGuid() } });
    Ensure(assignedInvalidAssignment.StatusCode == HttpStatusCode.BadRequest,
        "Owner cannot assign a service that does not belong to the current salon.");

    var assignedAssignProfessionalService = await PutWithCsrfAsync(
        mfaClient, $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/services",
        ownerAuthenticatedToken, new { serviceIds = new[] { salonServiceId } });
    Ensure(assignedAssignProfessionalService.StatusCode == HttpStatusCode.OK,
        "Owner can assign an active service from the same salon to a professional.");
    var assignedAssignedProfessional = await assignedAssignProfessionalService.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedAssignedProfessional.GetProperty("serviceIds").GetArrayLength() == 1 &&
           assignedAssignedProfessional.GetProperty("serviceIds")[0].GetGuid() == salonServiceId,
        "Professional profile returns only its explicitly assigned service IDs.");

    var assignedAssignedProfessionalList = await mfaClient.GetAsync("/api/v1/professionals");
    Ensure(assignedAssignedProfessionalList.StatusCode == HttpStatusCode.OK,
        "Owner can list professional profiles in the current salon.");
    var assignedAssignedProfessionalListBody = await assignedAssignedProfessionalList.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedAssignedProfessionalListBody.GetArrayLength() == 1 &&
           assignedAssignedProfessionalListBody[0].GetProperty("id").GetGuid() == assignedAssignedProfessionalProfileId,
        "Professional list contains the current salon's profile.");

    using var assignedAssignedProfessionalClient = mfaFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });
    var assignedAssignedProfessionalCsrf = await assignedAssignedProfessionalClient.GetAsync("/api/v1/auth/antiforgery");
    var assignedAssignedProfessionalCsrfBody = await assignedAssignedProfessionalCsrf.Content.ReadFromJsonAsync<JsonElement>();
    var assignedAssignedProfessionalRequestToken = assignedAssignedProfessionalCsrfBody.GetProperty("requestToken").GetString();
    Ensure(assignedAssignedProfessionalCsrf.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(assignedAssignedProfessionalRequestToken),
        "Professional client obtains an antiforgery token.");
    var assignedAssignedProfessionalLogin = await PostWithCsrfAsync(assignedAssignedProfessionalClient, "/api/v1/auth/login", assignedAssignedProfessionalRequestToken!, new
    {
        email = assignedAssignedProfessionalEmail,
        password = assignedAssignedProfessionalPassword
    });
    Ensure(assignedAssignedProfessionalLogin.StatusCode == HttpStatusCode.OK,
        "Professional identity can sign in with its own operational membership.");
    var assignedOwnProfessionalProfile = await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals/me");
    Ensure(assignedOwnProfessionalProfile.StatusCode == HttpStatusCode.OK,
        "Professional can read only its own profile through the /me route.");
    var assignedOwnProfessionalBody = await assignedOwnProfessionalProfile.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedOwnProfessionalBody.GetProperty("id").GetGuid() == assignedAssignedProfessionalProfileId &&
           assignedOwnProfessionalBody.GetProperty("serviceIds").GetArrayLength() == 1 &&
           assignedOwnProfessionalBody.GetProperty("serviceIds")[0].GetGuid() == salonServiceId,
        "Professional self-profile exposes only its own assigned services.");

    var assignedOwnServices = await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals/me/services");
    Ensure(assignedOwnServices.StatusCode == HttpStatusCode.OK,
        "Professional can retrieve details of only their own assigned services.");
    var assignedOwnServicesBody = await assignedOwnServices.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedOwnServicesBody.GetArrayLength() == 1 &&
           assignedOwnServicesBody[0].GetProperty("id").GetGuid() == salonServiceId &&
           assignedOwnServicesBody[0].GetProperty("name").GetString() == "Corte de cabelo",
        "Professional assigned-service details contain only the assigned salon service.");
    Ensure((await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals/eligible-members")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/services")).StatusCode == HttpStatusCode.Forbidden,
        "Professional cannot list other profiles, discover eligible members or access the unscoped service catalogue.");

    var professionalAudit = await ReadProfessionalAuditAsync(
        migratorConnectionString, ownerTenantId, assignedAssignedProfessionalProfileId);
    Ensure(professionalAudit.Contains("professional.created") &&
           professionalAudit.Contains("professional_service.assigned"),
        "Professional creation and service assignment are durably audited.");

    var updateSalonService = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/services/{salonServiceId}")
    {
        Content = JsonContent.Create(new
        {
            name = "Corte de cabelo",
            durationMinutes = 50,
            priceMinorUnits = 8000L,
            currency = "BRL",
            bufferBeforeMinutes = 5,
            bufferAfterMinutes = 15,
            isActive = true
        })
    };
    updateSalonService.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
    using var updateSalonServiceResponse = await mfaClient.SendAsync(updateSalonService);
    Ensure(updateSalonServiceResponse.StatusCode == HttpStatusCode.OK,
        "Owner can update the service's commercial and scheduling fields.");
    var updatedSalonService = await updateSalonServiceResponse.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(updatedSalonService.GetProperty("isActive").GetBoolean() &&
           updatedSalonService.GetProperty("priceMinorUnits").GetInt64() == 8000 &&
           updatedSalonService.GetProperty("durationMinutes").GetInt32() == 50,
        "Service update persists validated fields while preserving active state.");

    var archiveSalonService = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/services/{salonServiceId}")
    {
        Content = JsonContent.Create(new
        {
            name = "Corte de cabelo",
            durationMinutes = 50,
            priceMinorUnits = 8000L,
            currency = "BRL",
            bufferBeforeMinutes = 5,
            bufferAfterMinutes = 15,
            isActive = false
        })
    };
    archiveSalonService.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
    using var archiveSalonServiceResponse = await mfaClient.SendAsync(archiveSalonService);
    Ensure(archiveSalonServiceResponse.StatusCode == HttpStatusCode.OK,
        "Owner can archive a service without hard deletion.");
    var archivedSalonService = await archiveSalonServiceResponse.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(!archivedSalonService.GetProperty("isActive").GetBoolean(),
        "Archiving changes the active flag without deleting the service.");
    var serviceAudit = await ReadServiceAuditAsync(migratorConnectionString, ownerTenantId, salonServiceId);
    Ensure(serviceAudit.Contains("service.created") &&
           serviceAudit.Contains("service.updated") &&
           serviceAudit.Contains("service.archived"),
        "Service creation, update and archive are durably audited by a database trigger.");

    var ownerLogout = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/logout", ownerAuthenticatedToken, new { });
    Ensure(ownerLogout.StatusCode == HttpStatusCode.NoContent,
        "Authenticated owner can log out.");
    Ensure((await mfaClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Logout clears the authenticated application session.");
    var ownerAnonymousToken = await GetAntiforgeryRequestTokenAsync(mfaClient);

    var ownerMfaLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", ownerAnonymousToken, new
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
        mfaClient, "/api/v1/auth/mfa/challenge", ownerAnonymousToken, new
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

    var recoveryAuthenticatedToken = await GetAntiforgeryRequestTokenAsync(mfaClient);
    var replayLogout = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/logout", recoveryAuthenticatedToken, new { });
    Ensure(replayLogout.StatusCode == HttpStatusCode.NoContent,
        "Owner can terminate the recovery-code-authenticated session.");

    var afterRecoveryLogoutToken = await GetAntiforgeryRequestTokenAsync(mfaClient);
    var replayLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", afterRecoveryLogoutToken, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    Ensure(replayLogin.StatusCode == HttpStatusCode.Accepted,
        "Owner must complete MFA again after logout.");

    var replayChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", afterRecoveryLogoutToken, new
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

    var invalidTotpCode = (secondTotpCode[0] == '0' ? '1' : '0') + secondTotpCode[1..];
    var invalidTotpChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", afterRecoveryLogoutToken, new
        {
            code = invalidTotpCode,
            useRecoveryCode = false
        });
    Ensure(invalidTotpChallenge.StatusCode == HttpStatusCode.Unauthorized,
        "An invalid TOTP code is rejected without completing MFA.");
    var failedTotpAudit = await ReadSecurityAuditTypesAsync(migratorConnectionString, ownerUserId);
    Ensure(failedTotpAudit.Count(type => type == "operational_mfa.challenge_failed") == 2,
        "Invalid TOTP and replayed recovery-code attempts are each durably audited exactly once.");
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var owner = await users.FindByIdAsync(ownerUserId.ToString());
        Ensure(owner is not null && owner.AccessFailedCount == 2,
            "Invalid TOTP and replayed recovery-code attempts each increment the failed-attempt counter.");
    }

    var totpChallenge = await PostWithCsrfAsync(
        mfaClient, "/api/v1/auth/mfa/challenge", afterRecoveryLogoutToken, new
        {
            code = secondTotpCode,
            useRecoveryCode = false
        });
    Ensure(totpChallenge.StatusCode == HttpStatusCode.OK,
        "A fresh TOTP code completes the MFA challenge.");
    await using (var scope = mfaFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var owner = await users.FindByIdAsync(ownerUserId.ToString());
        Ensure(owner is not null && owner.AccessFailedCount == 0,
            "A successful TOTP challenge resets the accumulated failed-attempt counter.");
    }
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
    Ensure((await mfaClient.GetAsync("/api/v1/professionals/eligible-members")).StatusCode == HttpStatusCode.Forbidden,
        "Receptionist cannot discover identities or create professional profiles.");

    // Isolate role-promotion login attempts from the earlier MFA and professional clients.
    promotionKeyRing = Path.Combine(Path.GetTempPath(), "atendee-promotion-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(promotionKeyRing);
    promotionFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = promotionKeyRing
            });
        });
    });

    using var promotionClient = promotionFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var promotionEmail = "http-promotion-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string promotionPassword = "Atendee-Promotion-HTTP-2026!";
    var promotionTenantId = Guid.NewGuid();
    Guid promotionUserId;
    await using (var scope = promotionFactory.Services.CreateAsyncScope())
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

    await using (var scope = promotionFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var receptionist = await users.FindByIdAsync(promotionUserId.ToString());
        Ensure(receptionist is not null, "Promotion test identity remains available.");
        var enabled = await users.SetTwoFactorEnabledAsync(receptionist!, true);
        Ensure(enabled.Succeeded, "Promotion test identity completes MFA before role elevation.");
    }
    await UpdateMembershipAsync(migratorConnectionString, promotionUserId, roleId: 1, isActive: true);
    var promotedOwnerSession = await promotionClient.GetAsync("/api/v1/auth/me");
    Ensure(promotedOwnerSession.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
        "A password-only session is denied after a server-side MFA-ready Owner promotion (HTTP " +
        (int)promotedOwnerSession.StatusCode + ").");

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

    using var deactivationClient = roleMatrixFactory!.CreateClient(new WebApplicationFactoryClientOptions
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

    var managerEmail = "http-role-manager-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string managerPassword = "Atendee-Manager-Role-2026!";
    var managerTenantId = Guid.NewGuid();
    Guid managerUserId;
    await using (var scope = mfaFactory!.Services.CreateAsyncScope())
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
    await using (var scope = roleMatrixFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var manager = await users.FindByIdAsync(managerUserId.ToString());
        Ensure(manager is not null && manager.AccessFailedCount == 1,
            "An incorrect password increments the account failure counter together with its audit event.");
    }

    var managerLogin = await PostWithCsrfAsync(managerClient, "/api/v1/auth/login", managerToken!, new
    {
        email = managerEmail,
        password = managerPassword
    });
    var managerLoginBody = await managerLogin.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(managerLogin.StatusCode == HttpStatusCode.Accepted &&
           managerLoginBody.GetProperty("state").GetString() == "mfa_setup_required",
        "Manager without MFA receives only the restricted MFA-setup session.");
    await using (var scope = roleMatrixFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var manager = await users.FindByIdAsync(managerUserId.ToString());
        Ensure(manager is not null && manager.AccessFailedCount == 0,
            "A successful password verification resets the accumulated failed-attempt counter.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, managerUserId))
               .Count(type => type == "operational_auth.password_verified_mfa_pending") == 1,
        "Manager password verification that requires MFA setup is audited as pending exactly once.");
    Ensure((await managerClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Manager cannot use the application API before completing MFA.");

    var managerEnrollmentStart = await PostWithCsrfAsync(
        managerClient, "/api/v1/auth/mfa/enrollment/start", managerToken!, new { });
    Ensure(managerEnrollmentStart.StatusCode == HttpStatusCode.OK,
        "Manager can begin the required authenticator enrollment.");
    string managerEnrollmentCode;
    await using (var scope = mfaFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var manager = await users.FindByIdAsync(managerUserId.ToString());
        Ensure(manager is not null, "Manager identity remains available for authenticator enrollment.");
        managerEnrollmentCode = await users.GenerateTwoFactorTokenAsync(
            manager!, users.Options.Tokens.AuthenticatorTokenProvider);
    }
    var managerEnrollmentVerify = await PostWithCsrfAsync(
        managerClient, "/api/v1/auth/mfa/enrollment/verify", managerToken!, new
        {
            code = managerEnrollmentCode
        });
    Ensure(managerEnrollmentVerify.StatusCode == HttpStatusCode.OK &&
           (await managerClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.OK,
        "Manager receives an application session only after successful MFA enrollment.");
    var managerAuthenticatedToken = await GetAntiforgeryRequestTokenAsync(managerClient);


    var professionalEmail = "http-role-professional-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string professionalPassword = "Atendee-Professional-Role-2026!";
    var professionalTenantId = Guid.NewGuid();
    Guid professionalUserId;
    await using (var scope = mfaFactory!.Services.CreateAsyncScope())
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

    var managerProfessionalEmail = "http-role-manager-professional-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var managerProfessionalUserId = Guid.NewGuid();
    await using (var scope = mfaFactory!.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var managerProfessional = new OperationalIdentityUser
        {
            Id = managerProfessionalUserId,
            UserName = managerProfessionalEmail,
            Email = managerProfessionalEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(managerProfessional, "Atendee-Manager-Professional-2026!");
        Ensure(created.Succeeded, "Manager-tenant Professional identity is created independently of other salon fixtures.");
    }
    await SeedAdditionalMembershipAsync(migratorConnectionString, managerTenantId, managerProfessionalUserId, roleId: 4);
    var managerEligibleMembers = await managerClient.GetAsync("/api/v1/professionals/eligible-members");
    Ensure(managerEligibleMembers.StatusCode == HttpStatusCode.OK,
        "Manager can discover eligible Professional memberships in the current salon.");
    var managerEligibleMembersBody = await managerEligibleMembers.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(managerEligibleMembersBody.GetArrayLength() == 1 &&
           managerEligibleMembersBody[0].GetProperty("identityUserId").GetGuid() == managerProfessionalUserId,
        "Manager candidate discovery is scoped to its own salon and returns only an unprofiled Professional.");
    var managerCreateProfessional = await PostWithCsrfAsync(
        managerClient, "/api/v1/professionals", managerAuthenticatedToken, new
        {
            identityUserId = managerProfessionalUserId
        });
    Ensure(managerCreateProfessional.StatusCode == HttpStatusCode.Created,
        "Manager can create a profile for an active Professional membership in the current salon.");
    var managerProfessionalList = await managerClient.GetAsync("/api/v1/professionals");
    Ensure(managerProfessionalList.StatusCode == HttpStatusCode.OK &&
           (await managerProfessionalList.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength() == 1,
        "Manager can list the professional team for the current salon.");
    Ensure((await managerClient.GetAsync("/api/v1/services")).StatusCode == HttpStatusCode.OK,
        "Manager can read the current salon's service catalogue for assignments.");

    // Force the mandatory pending-MFA audit write to fail after PasswordSignInAsync
    // has produced the temporary two-factor cookie. The failed request must not
    // leave a usable challenge session behind.
    var auditFailureEmail = "http-pending-audit-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string auditFailurePassword = "Atendee-Audit-Failure-HTTP-2026!";
    var auditFailureTenantId = Guid.NewGuid();
    Guid auditFailureUserId;
    string auditFailureRecoveryCode;
    await using (var scope = roleMatrixFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var auditFailureUser = new OperationalIdentityUser
        {
            UserName = auditFailureEmail,
            Email = auditFailureEmail,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(auditFailureUser, auditFailurePassword);
        Ensure(created.Succeeded, "Synthetic MFA identity for audit-failure checks is created.");
        auditFailureUserId = auditFailureUser.Id;

        var authenticatorKey = await users.ResetAuthenticatorKeyAsync(auditFailureUser);
        Ensure(authenticatorKey.Succeeded, "Audit-failure identity receives an authenticator key.");
        var enabled = await users.SetTwoFactorEnabledAsync(auditFailureUser, true);
        Ensure(enabled.Succeeded, "Audit-failure identity is MFA-enabled before login.");
        var auditFailureRecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(auditFailureUser, 10);
        Ensure(auditFailureRecoveryCodes is not null && auditFailureRecoveryCodes.Any(),
            "Audit-failure identity receives a one-time recovery code for the cookie-leak probe.");
        auditFailureRecoveryCode = auditFailureRecoveryCodes!.First();
    }
    await SeedMembershipAsync(migratorConnectionString, auditFailureTenantId, auditFailureUserId, roleId: 2);

    using var auditFailureClient = roleMatrixFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var auditFailureToken = await GetAntiforgeryRequestTokenAsync(auditFailureClient);
    await SetPendingMfaAuditFailureTriggerAsync(migratorConnectionString, enabled: true);
    HttpResponseMessage auditFailureLogin;
    try
    {
        auditFailureLogin = await PostWithCsrfAsync(auditFailureClient, "/api/v1/auth/login",
            auditFailureToken, new
            {
                email = auditFailureEmail,
                password = auditFailurePassword
            });
    }
    finally
    {
        await SetPendingMfaAuditFailureTriggerAsync(migratorConnectionString, enabled: false);
    }

    using (auditFailureLogin)
    {
        Ensure(auditFailureLogin.StatusCode == HttpStatusCode.InternalServerError,
            "A failing mandatory pending-MFA audit aborts the login request.");
    }
    var auditFailureEvents = await ReadSecurityAuditTypesAsync(migratorConnectionString, auditFailureUserId);
    Ensure(!auditFailureEvents.Contains("operational_auth.password_verified_mfa_pending"),
        "A rejected pending-MFA audit event is not persisted.");

    var auditFailureChallengeToken = await GetAntiforgeryRequestTokenAsync(auditFailureClient);
    var leakedChallenge = await PostWithCsrfAsync(auditFailureClient, "/api/v1/auth/mfa/challenge",
        auditFailureChallengeToken, new
        {
            code = auditFailureRecoveryCode,
            useRecoveryCode = true
        });
    Ensure(leakedChallenge.StatusCode == HttpStatusCode.Unauthorized,
        "A failed pending-MFA audit does not leave a usable temporary two-factor cookie.");
    Ensure((await auditFailureClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "A failed pending-MFA audit cannot be upgraded to an application session.");

    // Sequential failures must include the threshold-causing attempt in the audit,
    // while the following request is rejected by lockout before it can add an event.
    var sequentialLockoutEmail = "http-sequential-lockout-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string sequentialLockoutPassword = "Atendee-Sequential-Lockout-2026!";
    Guid sequentialLockoutUserId;
    var sequentialMaxAttempts = 0;
    sequentialLockoutKeyRing = Path.Combine(Path.GetTempPath(), "atendee-sequential-lockout-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(sequentialLockoutKeyRing);
    using var sequentialLockoutFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = sequentialLockoutKeyRing
            });
        });
    });
    await using (var scope = sequentialLockoutFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var sequentialUser = new OperationalIdentityUser
        {
            UserName = sequentialLockoutEmail,
            Email = sequentialLockoutEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var created = await users.CreateAsync(sequentialUser, sequentialLockoutPassword);
        Ensure(created.Succeeded, "Synthetic identity for sequential lockout checks is created.");
        sequentialLockoutUserId = sequentialUser.Id;
        sequentialMaxAttempts = users.Options.Lockout.MaxFailedAccessAttempts;
        Ensure(sequentialMaxAttempts == 5, "Sequential lockout uses the configured five-attempt threshold.");
    }

    using var sequentialLockoutClient = sequentialLockoutFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var sequentialLockoutToken = await GetAntiforgeryRequestTokenAsync(sequentialLockoutClient);
    for (var attempt = 1; attempt <= sequentialMaxAttempts; attempt++)
    {
        using var response = await PostWithCsrfAsync(
            sequentialLockoutClient, "/api/v1/auth/login", sequentialLockoutToken, new
            {
                email = sequentialLockoutEmail,
                password = "Wrong-Sequential-Lockout-2026!"
            });
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "Each sequential incorrect-password attempt, including the threshold attempt, is generically rejected.");
    }
    await using (var scope = sequentialLockoutFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var sequentialUser = await users.FindByIdAsync(sequentialLockoutUserId.ToString());
        Ensure(sequentialUser is not null && await users.IsLockedOutAsync(sequentialUser),
            "The sequential threshold-causing failure locks the account.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, sequentialLockoutUserId))
               .Count(type => type == "operational_auth.login_failed") == sequentialMaxAttempts,
        "Every sequential failed-password attempt up to and including lockout is audited exactly once.");

    // Concurrent wrong-password attempts must serialize on the Identity row.
    // Seed three failures, then race two more: both requests began before lockout,
    // so both must be counted and audited; the second reaches the configured limit.
    var lockoutEmail = "http-concurrent-lockout-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string lockoutPassword = "Atendee-Concurrent-Lockout-2026!";
    Guid lockoutUserId;
    lockoutKeyRing = Path.Combine(Path.GetTempPath(), "atendee-lockout-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(lockoutKeyRing);
    using var lockoutFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = lockoutKeyRing
            });
        });
    });
    await using (var scope = lockoutFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var lockoutUser = new OperationalIdentityUser
        {
            UserName = lockoutEmail,
            Email = lockoutEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var created = await users.CreateAsync(lockoutUser, lockoutPassword);
        Ensure(created.Succeeded, "Synthetic identity for concurrent lockout checks is created.");
        lockoutUserId = lockoutUser.Id;
        var maxAttempts = users.Options.Lockout.MaxFailedAccessAttempts;
        Ensure(maxAttempts == 5, "Operational login retains the configured five-attempt lockout threshold.");
        lockoutUser.AccessFailedCount = maxAttempts - 2;
        var seeded = await users.UpdateAsync(lockoutUser);
        Ensure(seeded.Succeeded, "Concurrent lockout identity starts two failures below the threshold.");
    }

    using var concurrentLockoutClientA = lockoutFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var concurrentLockoutClientB = lockoutFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var concurrentLockoutTokenA = await GetAntiforgeryRequestTokenAsync(concurrentLockoutClientA);
    var concurrentLockoutTokenB = await GetAntiforgeryRequestTokenAsync(concurrentLockoutClientB);

    await SetLoginFailureDelayTriggerAsync(migratorConnectionString, lockoutUserId, enabled: true);
    HttpResponseMessage[] concurrentLockoutResponses;
    try
    {
        concurrentLockoutResponses = await Task.WhenAll(
            PostWithCsrfAsync(concurrentLockoutClientA, "/api/v1/auth/login", concurrentLockoutTokenA, new
            {
                email = lockoutEmail,
                password = "Wrong-Concurrent-Lockout-2026!"
            }),
            PostWithCsrfAsync(concurrentLockoutClientB, "/api/v1/auth/login", concurrentLockoutTokenB, new
            {
                email = lockoutEmail,
                password = "Wrong-Concurrent-Lockout-2026!"
            }));
    }
    finally
    {
        await SetLoginFailureDelayTriggerAsync(migratorConnectionString, lockoutUserId, enabled: false);
    }

    foreach (var response in concurrentLockoutResponses)
    {
        using (response)
            Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
                "Concurrent incorrect-password attempts are rejected with the generic login response.");
    }

    await using (var scope = lockoutFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var lockedUser = await users.FindByIdAsync(lockoutUserId.ToString());
        Ensure(lockedUser is not null && await users.IsLockedOutAsync(lockedUser),
            "Both concurrent failures are counted; the second attempt reaches the five-failure lockout threshold.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, lockoutUserId))
               .Count(type => type == "operational_auth.login_failed") == 2,
        "Both concurrent attempts begun before lockout receive exactly one login-failure audit event.");

    using (var alreadyLockedOutAttempt = await PostWithCsrfAsync(
        concurrentLockoutClientA, "/api/v1/auth/login", concurrentLockoutTokenA, new
        {
            email = lockoutEmail,
            password = "Wrong-After-Lockout-2026!"
        }))
    {
        Ensure(alreadyLockedOutAttempt.StatusCode == HttpStatusCode.Unauthorized,
            "A subsequent login attempt against an already-locked account remains generically rejected.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, lockoutUserId))
               .Count(type => type == "operational_auth.login_failed") == 2,
        "Attempts begun after lockout do not create unbounded login-failure audit rows.");

    // Password failures and MFA failures share one Identity counter. Race them
    // for both an MFA challenge identity and an enrollment-pending identity.
    var challengeCrossFlowEmail = "http-crossflow-challenge-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var enrollmentCrossFlowEmail = "http-crossflow-enrollment-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string crossFlowPassword = "Atendee-Cross-Flow-2026!";
    Guid challengeCrossFlowUserId;
    Guid enrollmentCrossFlowUserId;
    crossFlowKeyRing = Path.Combine(Path.GetTempPath(), "atendee-crossflow-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(crossFlowKeyRing);
    using var crossFlowFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = crossFlowKeyRing
            });
        });
    });
    await using (var scope = crossFlowFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var challengeUser = new OperationalIdentityUser
        {
            UserName = challengeCrossFlowEmail,
            Email = challengeCrossFlowEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var challengeCreated = await users.CreateAsync(challengeUser, crossFlowPassword);
        Ensure(challengeCreated.Succeeded, "Cross-flow MFA challenge identity is created.");
        challengeCrossFlowUserId = challengeUser.Id;
        var authenticatorKey = await users.ResetAuthenticatorKeyAsync(challengeUser);
        Ensure(authenticatorKey.Succeeded, "Cross-flow MFA challenge identity receives an authenticator key.");
        var enabled = await users.SetTwoFactorEnabledAsync(challengeUser, true);
        Ensure(enabled.Succeeded, "Cross-flow MFA challenge identity is MFA-enabled.");
        var challengeRecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(challengeUser, 10);
        Ensure(challengeRecoveryCodes is not null && challengeRecoveryCodes.Any(),
            "Cross-flow MFA challenge identity receives recovery codes.");

        var enrollmentUser = new OperationalIdentityUser
        {
            UserName = enrollmentCrossFlowEmail,
            Email = enrollmentCrossFlowEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var enrollmentCreated = await users.CreateAsync(enrollmentUser, crossFlowPassword);
        Ensure(enrollmentCreated.Succeeded, "Cross-flow MFA enrollment identity is created.");
        enrollmentCrossFlowUserId = enrollmentUser.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), challengeCrossFlowUserId, roleId: 2);

    using var challengeCrossFlowClient = crossFlowFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var challengeCrossFlowWrongLoginClient = crossFlowFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var enrollmentCrossFlowClient = crossFlowFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var enrollmentCrossFlowWrongLoginClient = crossFlowFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var challengeCrossFlowToken = await GetAntiforgeryRequestTokenAsync(challengeCrossFlowClient);
    var challengeWrongLoginToken = await GetAntiforgeryRequestTokenAsync(challengeCrossFlowWrongLoginClient);
    var enrollmentCrossFlowToken = await GetAntiforgeryRequestTokenAsync(enrollmentCrossFlowClient);
    var enrollmentWrongLoginToken = await GetAntiforgeryRequestTokenAsync(enrollmentCrossFlowWrongLoginClient);

    using (var response = await PostWithCsrfAsync(challengeCrossFlowClient, "/api/v1/auth/login",
        challengeCrossFlowToken, new { email = challengeCrossFlowEmail, password = crossFlowPassword }))
        Ensure(response.StatusCode == HttpStatusCode.Accepted,
            "MFA-enabled cross-flow identity receives a pending challenge cookie.");
    using (var response = await PostWithCsrfAsync(enrollmentCrossFlowClient, "/api/v1/auth/login",
        enrollmentCrossFlowToken, new { email = enrollmentCrossFlowEmail, password = crossFlowPassword }))
        Ensure(response.StatusCode == HttpStatusCode.Accepted,
            "Unenrolled cross-flow identity receives an MFA setup cookie.");

    using (var response = await PostWithCsrfAsync(enrollmentCrossFlowClient,
        "/api/v1/auth/mfa/enrollment/start", enrollmentCrossFlowToken, new { }))
        Ensure(response.StatusCode == HttpStatusCode.OK,
            "Cross-flow enrollment session initializes its TOTP secret.");
    var enrollmentCrossFlowPostStartToken = await GetAntiforgeryRequestTokenAsync(enrollmentCrossFlowClient);

    string invalidChallengeCrossFlowCode;
    string invalidEnrollmentCrossFlowCode;
    await using (var scope = crossFlowFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var challengeUser = await users.FindByIdAsync(challengeCrossFlowUserId.ToString());
        var enrollmentUser = await users.FindByIdAsync(enrollmentCrossFlowUserId.ToString());
        Ensure(challengeUser is not null && enrollmentUser is not null,
            "Both cross-flow identities remain available after their pending-MFA logins.");

        var challengeValidCode = await users.GenerateTwoFactorTokenAsync(
            challengeUser!, users.Options.Tokens.AuthenticatorTokenProvider);
        invalidChallengeCrossFlowCode = (challengeValidCode[0] == '0' ? '1' : '0') + challengeValidCode[1..];

        var enrollmentValidCode = await users.GenerateTwoFactorTokenAsync(
            enrollmentUser!, users.Options.Tokens.AuthenticatorTokenProvider);
        invalidEnrollmentCrossFlowCode = (enrollmentValidCode[0] == '0' ? '1' : '0') + enrollmentValidCode[1..];

        challengeUser!.AccessFailedCount = 2;
        enrollmentUser!.AccessFailedCount = 3;
        var challengeSeeded = await users.UpdateAsync(challengeUser);
        var enrollmentSeeded = await users.UpdateAsync(enrollmentUser);
        Ensure(challengeSeeded.Succeeded && enrollmentSeeded.Succeeded,
            "Both cross-flow identities are seeded below the lockout threshold.");
    }

    await SetLoginFailureDelayTriggerAsync(migratorConnectionString, challengeCrossFlowUserId, enabled: true);
    HttpResponseMessage[] challengeCrossFlowResponses;
    try
    {
        challengeCrossFlowResponses = await Task.WhenAll(
            PostWithCsrfAsync(challengeCrossFlowClient, "/api/v1/auth/mfa/challenge", challengeCrossFlowToken,
                new { code = invalidChallengeCrossFlowCode, useRecoveryCode = false }),
            PostWithCsrfAsync(challengeCrossFlowClient, "/api/v1/auth/mfa/challenge", challengeCrossFlowToken,
                new { code = "invalid-recovery-code", useRecoveryCode = true }),
            PostWithCsrfAsync(challengeCrossFlowWrongLoginClient, "/api/v1/auth/login", challengeWrongLoginToken,
                new { email = challengeCrossFlowEmail, password = "Wrong-Cross-Flow-Password-2026!" }));
    }
    finally
    {
        await SetLoginFailureDelayTriggerAsync(migratorConnectionString, challengeCrossFlowUserId, enabled: false);
    }
    foreach (var response in challengeCrossFlowResponses)
    {
        using (response)
            Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
                "Concurrent password and TOTP failures are both generically rejected.");
    }
    await using (var scope = crossFlowFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(challengeCrossFlowUserId.ToString());
        Ensure(user is not null && await users.IsLockedOutAsync(user),
            "Concurrent password and TOTP failures both count toward the shared five-attempt lockout.");
    }
    var challengeCrossFlowEvents = await ReadSecurityAuditTypesAsync(migratorConnectionString, challengeCrossFlowUserId);
    Ensure(challengeCrossFlowEvents.Count(type => type == "operational_auth.login_failed") == 1 &&
           challengeCrossFlowEvents.Count(type => type == "operational_mfa.challenge_failed") == 2,
        "Cross-flow password, TOTP and recovery-code failures each write exactly one mandatory audit event.");
    using (var response = await PostWithCsrfAsync(challengeCrossFlowClient, "/api/v1/auth/mfa/challenge",
        challengeCrossFlowToken, new { code = invalidChallengeCrossFlowCode, useRecoveryCode = false }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "An already-locked MFA challenge is rejected before another counter or audit mutation.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, challengeCrossFlowUserId))
               .Count(type => type == "operational_mfa.challenge_failed") == 2,
        "Already-locked MFA challenges do not create extra failure audit events.");

    await SetLoginFailureDelayTriggerAsync(migratorConnectionString, enrollmentCrossFlowUserId, enabled: true);
    HttpResponseMessage[] enrollmentCrossFlowResponses;
    try
    {
        enrollmentCrossFlowResponses = await Task.WhenAll(
            PostWithCsrfAsync(enrollmentCrossFlowClient, "/api/v1/auth/mfa/enrollment/verify",
                enrollmentCrossFlowPostStartToken, new { code = invalidEnrollmentCrossFlowCode }),
            PostWithCsrfAsync(enrollmentCrossFlowWrongLoginClient, "/api/v1/auth/login",
                enrollmentWrongLoginToken, new
                {
                    email = enrollmentCrossFlowEmail,
                    password = "Wrong-Cross-Flow-Password-2026!"
                }));
    }
    finally
    {
        await SetLoginFailureDelayTriggerAsync(migratorConnectionString, enrollmentCrossFlowUserId, enabled: false);
    }
    Ensure(enrollmentCrossFlowResponses.Length == 2 &&
           (enrollmentCrossFlowResponses[0].StatusCode == HttpStatusCode.BadRequest ||
            enrollmentCrossFlowResponses[0].StatusCode == HttpStatusCode.Unauthorized) &&
           enrollmentCrossFlowResponses[1].StatusCode == HttpStatusCode.Unauthorized,
        "Concurrent enrollment and password failures return their expected generic/invalid-code responses.");
    foreach (var response in enrollmentCrossFlowResponses)
        response.Dispose();
    await using (var scope = crossFlowFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(enrollmentCrossFlowUserId.ToString());
        Ensure(user is not null && await users.IsLockedOutAsync(user),
            "Concurrent password and enrollment failures both count toward the shared five-attempt lockout.");
    }
    var enrollmentCrossFlowEvents = await ReadSecurityAuditTypesAsync(migratorConnectionString, enrollmentCrossFlowUserId);
    Ensure(enrollmentCrossFlowEvents.Count(type => type == "operational_auth.login_failed") == 1 &&
           enrollmentCrossFlowEvents.Count(type => type == "operational_mfa.enrollment_failed") == 1,
        "Cross-flow password and enrollment failures each write exactly one mandatory audit event.");
    using (var response = await PostWithCsrfAsync(enrollmentCrossFlowClient,
        "/api/v1/auth/mfa/enrollment/verify", enrollmentCrossFlowPostStartToken,
        new { code = invalidEnrollmentCrossFlowCode }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "An already-locked MFA enrollment session is rejected before another counter or audit mutation.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, enrollmentCrossFlowUserId))
               .Count(type => type == "operational_mfa.enrollment_failed") == 1,
        "An already-locked MFA enrollment attempt does not create an extra failure audit event.");

    // Temporary MFA cookies must be bound to the security stamp that existed
    // when the password was verified. Revoking sessions or disabling the account
    // before MFA completion must invalidate pending challenge/setup cookies.
    var revokedChallengeEmail = "http-revoked-challenge-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var revokedSetupEmail = "http-revoked-setup-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var disabledSetupEmail = "http-disabled-setup-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string revocationPassword = "Atendee-Revocation-Cookie-2026!";
    Guid revokedChallengeUserId;
    Guid revokedSetupUserId;
    Guid disabledSetupUserId;
    revocationKeyRing = Path.Combine(Path.GetTempPath(), "atendee-revocation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(revocationKeyRing);
    using var revocationFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = revocationKeyRing
            });
        });
    });
    await using (var scope = revocationFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var challengeUser = new OperationalIdentityUser
        {
            UserName = revokedChallengeEmail,
            Email = revokedChallengeEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var challengeCreated = await users.CreateAsync(challengeUser, revocationPassword);
        Ensure(challengeCreated.Succeeded, "Pending-challenge revocation identity is created.");
        revokedChallengeUserId = challengeUser.Id;
        var authenticatorKey = await users.ResetAuthenticatorKeyAsync(challengeUser);
        Ensure(authenticatorKey.Succeeded, "Pending-challenge revocation identity receives an authenticator key.");
        var enabled = await users.SetTwoFactorEnabledAsync(challengeUser, true);
        Ensure(enabled.Succeeded, "Pending-challenge revocation identity is MFA-enabled.");

        var setupUser = new OperationalIdentityUser
        {
            UserName = revokedSetupEmail,
            Email = revokedSetupEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var setupCreated = await users.CreateAsync(setupUser, revocationPassword);
        Ensure(setupCreated.Succeeded, "Pending-setup revocation identity is created.");
        revokedSetupUserId = setupUser.Id;

        var disabledUser = new OperationalIdentityUser
        {
            UserName = disabledSetupEmail,
            Email = disabledSetupEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var disabledCreated = await users.CreateAsync(disabledUser, revocationPassword);
        Ensure(disabledCreated.Succeeded, "Pending-setup disable identity is created.");
        disabledSetupUserId = disabledUser.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), revokedChallengeUserId, roleId: 2);

    using var revokedChallengeClient = revocationFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var revokedSetupClient = revocationFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    using var disabledSetupClient = revocationFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });
    var revokedChallengeToken = await GetAntiforgeryRequestTokenAsync(revokedChallengeClient);
    var revokedSetupToken = await GetAntiforgeryRequestTokenAsync(revokedSetupClient);
    var disabledSetupToken = await GetAntiforgeryRequestTokenAsync(disabledSetupClient);

    using (var response = await PostWithCsrfAsync(revokedChallengeClient, "/api/v1/auth/login",
        revokedChallengeToken, new { email = revokedChallengeEmail, password = revocationPassword }))
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();
        Ensure(response.StatusCode == HttpStatusCode.Accepted &&
               cookies.Any(value => value.StartsWith("__Host-Atendee.MfaChallenge=", StringComparison.Ordinal)),
            "MFA challenge identity receives a short-lived password-verified challenge cookie.");
    }
    using (var response = await PostWithCsrfAsync(revokedSetupClient, "/api/v1/auth/login",
        revokedSetupToken, new { email = revokedSetupEmail, password = revocationPassword }))
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();
        Ensure(response.StatusCode == HttpStatusCode.Accepted &&
               cookies.Any(value => value.StartsWith("__Host-Atendee.MfaSetup=", StringComparison.Ordinal)),
            "Unenrolled identity receives a short-lived MFA setup cookie before session revocation.");
    }
    using (var response = await PostWithCsrfAsync(disabledSetupClient, "/api/v1/auth/login",
        disabledSetupToken, new { email = disabledSetupEmail, password = revocationPassword }))
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();
        Ensure(response.StatusCode == HttpStatusCode.Accepted &&
               cookies.Any(value => value.StartsWith("__Host-Atendee.MfaSetup=", StringComparison.Ordinal)),
            "Unenrolled identity receives a short-lived MFA setup cookie before disablement.");
    }

    var revokedChallengePostLoginToken = await GetAntiforgeryRequestTokenAsync(revokedChallengeClient);
    var revokedSetupPostLoginToken = await GetAntiforgeryRequestTokenAsync(revokedSetupClient);
    var disabledSetupPostLoginToken = await GetAntiforgeryRequestTokenAsync(disabledSetupClient);

    await using (var scope = revocationFactory.Services.CreateAsyncScope())
    {
        var accountSecurity = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        Ensure(await accountSecurity.RevokeSessionsAsync(revokedChallengeUserId),
            "Session revocation commits for the pending MFA challenge identity.");
        Ensure(await accountSecurity.RevokeSessionsAsync(revokedSetupUserId),
            "Session revocation commits for the pending MFA setup identity.");
        Ensure(await accountSecurity.DisableAsync(disabledSetupUserId),
            "Account disable commits for the pending MFA setup identity.");
    }
    await using (var scope = revocationFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var revokedChallengeUser = await users.FindByIdAsync(revokedChallengeUserId.ToString());
        var revokedSetupUser = await users.FindByIdAsync(revokedSetupUserId.ToString());
        var disabledSetupUser = await users.FindByIdAsync(disabledSetupUserId.ToString());
        Ensure(revokedChallengeUser is not null && !await users.IsLockedOutAsync(revokedChallengeUser) &&
               revokedSetupUser is not null && !await users.IsLockedOutAsync(revokedSetupUser),
            "Session revocation changes the security stamp without locking either pending-MFA identity.");
        Ensure(disabledSetupUser is not null && await users.IsLockedOutAsync(disabledSetupUser),
            "Account disablement applies the expected lockout state.");
    }

    string validRevokedChallengeCode;
    await using (var scope = revocationFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var challengeUser = await users.FindByIdAsync(revokedChallengeUserId.ToString());
        Ensure(challengeUser is not null, "Revoked MFA challenge identity remains persisted.");
        validRevokedChallengeCode = await users.GenerateTwoFactorTokenAsync(
            challengeUser!, users.Options.Tokens.AuthenticatorTokenProvider);
    }

    using (var response = await PostWithCsrfAsync(revokedChallengeClient, "/api/v1/auth/mfa/challenge",
        revokedChallengePostLoginToken, new { code = validRevokedChallengeCode, useRecoveryCode = false }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "A pending MFA challenge cannot create a new application session after session revocation.");
    Ensure((await revokedChallengeClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "A revoked pending MFA challenge cannot establish an authenticated application session.");

    using (var response = await PostWithCsrfAsync(revokedSetupClient, "/api/v1/auth/mfa/enrollment/start",
        revokedSetupPostLoginToken, new { }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "A pending MFA setup cookie is rejected after session revocation changes the security stamp.");
    using (var response = await PostWithCsrfAsync(disabledSetupClient, "/api/v1/auth/mfa/enrollment/start",
        disabledSetupPostLoginToken, new { }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "A pending MFA setup cookie is rejected after the account is disabled.");

    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, revokedChallengeUserId))
               .Count(type => type == "operational_account.sessions_revoked") == 1,
        "Pending MFA challenge session revocation is audited exactly once.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, revokedSetupUserId))
               .Count(type => type == "operational_account.sessions_revoked") == 1,
        "Pending MFA setup session revocation is audited exactly once.");
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, disabledSetupUserId))
               .Count(type => type == "operational_account.disabled") == 1,
        "Pending MFA setup account disablement is audited exactly once.");

    // Exercise the narrow windows between a successful authentication transaction,
    // its mandatory audit write and the eventual cookie response. Test-only advisory
    // locks pause the audit insert while a concurrent revoke/disable commits.
    var raceLoginEmail = "http-race-login-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var raceChallengeEmail = "http-race-challenge-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var raceEnrollmentEmail = "http-race-enrollment-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string racePassword = "Atendee-InFlight-Race-2026!";
    Guid raceLoginUserId;
    Guid raceChallengeUserId;
    Guid raceEnrollmentUserId;
    using var raceFactory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
    {
        web.UseContentRoot(apiContentRoot);
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = identityConnectionString,
                ["ConnectionStrings:Tenant"] = tenantConnectionString,
                ["DataProtection:KeyRingPath"] = revocationKeyRing
            });
        });
    });
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var loginUser = new OperationalIdentityUser
        {
            UserName = raceLoginEmail,
            Email = raceLoginEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var loginCreated = await users.CreateAsync(loginUser, racePassword);
        Ensure(loginCreated.Succeeded, "In-flight password-verification identity is created.");
        raceLoginUserId = loginUser.Id;
        var loginKey = await users.ResetAuthenticatorKeyAsync(loginUser);
        var loginMfa = await users.SetTwoFactorEnabledAsync(loginUser, true);
        loginUser.AccessFailedCount = 1;
        var loginSeed = await users.UpdateAsync(loginUser);
        Ensure(loginKey.Succeeded && loginMfa.Succeeded && loginSeed.Succeeded,
            "In-flight password-verification identity is MFA-enabled and seeded for a successful lockout-counter reset.");

        var challengeUser = new OperationalIdentityUser
        {
            UserName = raceChallengeEmail,
            Email = raceChallengeEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var challengeCreated = await users.CreateAsync(challengeUser, racePassword);
        Ensure(challengeCreated.Succeeded, "In-flight MFA challenge identity is created.");
        raceChallengeUserId = challengeUser.Id;
        var challengeKey = await users.ResetAuthenticatorKeyAsync(challengeUser);
        var challengeMfa = await users.SetTwoFactorEnabledAsync(challengeUser, true);
        Ensure(challengeKey.Succeeded && challengeMfa.Succeeded,
            "In-flight MFA challenge identity is MFA-enabled.");

        var enrollmentUser = new OperationalIdentityUser
        {
            UserName = raceEnrollmentEmail,
            Email = raceEnrollmentEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var enrollmentCreated = await users.CreateAsync(enrollmentUser, racePassword);
        Ensure(enrollmentCreated.Succeeded, "In-flight MFA enrollment identity is created.");
        raceEnrollmentUserId = enrollmentUser.Id;
    }
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceLoginUserId, roleId: 2);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceChallengeUserId, roleId: 2);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceEnrollmentUserId, roleId: 1);

    using var raceLoginClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceLoginToken = await GetAntiforgeryRequestTokenAsync(raceLoginClient);
    await SetIdentityUpdateDelayTriggerAsync(migratorConnectionString, raceLoginUserId, enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var loginTask = PostWithCsrfAsync(raceLoginClient, "/api/v1/auth/login", raceLoginToken,
            new { email = raceLoginEmail, password = racePassword });
        try
        {
            await WaitForIdentityUpdateDelayAsync(migratorConnectionString);
            var revokeTask = Task.Run(async () =>
            {
                await using var scope = raceFactory.Services.CreateAsyncScope();
                var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
                return await security.RevokeSessionsAsync(raceLoginUserId);
            });
            await WaitForIdentityRowLockAsync(migratorConnectionString);
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await loginTask;
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : Array.Empty<string>();
            Ensure(response.StatusCode == HttpStatusCode.Accepted &&
                   cookies.Any(value => value.StartsWith("__Host-Atendee.MfaChallenge=", StringComparison.Ordinal)),
                "Password verification may finish its response, but its challenge cookie is bound to the pre-revocation stamp.");
            Ensure(await revokeTask,
                "Session revocation commits immediately after the in-flight password-verification transaction.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetIdentityUpdateDelayTriggerAsync(migratorConnectionString, raceLoginUserId, enabled: false);
    var raceLoginPostToken = await GetAntiforgeryRequestTokenAsync(raceLoginClient);
    string raceLoginChallengeCode;
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(raceLoginUserId.ToString());
        Ensure(user is not null, "Password-flow revoked MFA identity remains persisted.");
        raceLoginChallengeCode = await users.GenerateTwoFactorTokenAsync(
            user!, users.Options.Tokens.AuthenticatorTokenProvider);
    }
    using (var response = await PostWithCsrfAsync(raceLoginClient, "/api/v1/auth/mfa/challenge",
        raceLoginPostToken, new { code = raceLoginChallengeCode, useRecoveryCode = false }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "A challenge cookie issued after concurrent password-flow revocation cannot complete MFA.");
    Ensure((await raceLoginClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Password-flow revocation leaves no usable application session.");

    using var raceChallengeClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceChallengeToken = await GetAntiforgeryRequestTokenAsync(raceChallengeClient);
    using (var response = await PostWithCsrfAsync(raceChallengeClient, "/api/v1/auth/login",
        raceChallengeToken, new { email = raceChallengeEmail, password = racePassword }))
        Ensure(response.StatusCode == HttpStatusCode.Accepted,
            "In-flight challenge identity receives its initial pending-MFA ticket.");
    var raceChallengePostToken = await GetAntiforgeryRequestTokenAsync(raceChallengeClient);
    string raceChallengeCode;
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(raceChallengeUserId.ToString());
        Ensure(user is not null, "In-flight challenge identity remains available.");
        raceChallengeCode = await users.GenerateTwoFactorTokenAsync(
            user!, users.Options.Tokens.AuthenticatorTokenProvider);
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceChallengeUserId,
        "operational_mfa.totp_challenge_succeeded", enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var challengeTask = PostWithCsrfAsync(raceChallengeClient, "/api/v1/auth/mfa/challenge",
            raceChallengePostToken, new { code = raceChallengeCode, useRecoveryCode = false });
        try
        {
            await WaitForSecurityAuditDelayAsync(migratorConnectionString);
            var revokeTask = Task.Run(async () =>
            {
                await using var scope = raceFactory.Services.CreateAsyncScope();
                var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
                return await security.RevokeSessionsAsync(raceChallengeUserId);
            });
            await WaitForIdentityRowLockAsync(migratorConnectionString);
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await challengeTask;
            Ensure(response.StatusCode == HttpStatusCode.OK,
                "A valid MFA challenge completes while concurrent session revocation waits on the identity lock.");
            Ensure(await revokeTask,
                "Session revocation commits immediately after the in-flight MFA challenge transaction.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceChallengeUserId,
        "operational_mfa.totp_challenge_succeeded", enabled: false);
    Ensure((await raceChallengeClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "An application cookie emitted by an MFA challenge racing with revocation is unusable.");

    using var raceEnrollmentClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceEnrollmentToken = await GetAntiforgeryRequestTokenAsync(raceEnrollmentClient);
    using (var response = await PostWithCsrfAsync(raceEnrollmentClient, "/api/v1/auth/login",
        raceEnrollmentToken, new { email = raceEnrollmentEmail, password = racePassword }))
        Ensure(response.StatusCode == HttpStatusCode.Accepted,
            "In-flight enrollment identity receives its initial MFA setup ticket.");
    var raceEnrollmentSetupToken = await GetAntiforgeryRequestTokenAsync(raceEnrollmentClient);
    using var raceEnrollmentStart = await PostWithCsrfAsync(raceEnrollmentClient,
        "/api/v1/auth/mfa/enrollment/start", raceEnrollmentSetupToken, new { });
    Ensure(raceEnrollmentStart.StatusCode == HttpStatusCode.OK,
        "In-flight enrollment identity initializes its authenticator key.");
    var raceEnrollmentPostToken = await GetAntiforgeryRequestTokenAsync(raceEnrollmentClient);
    string raceEnrollmentCode;
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(raceEnrollmentUserId.ToString());
        Ensure(user is not null, "In-flight enrollment identity remains available.");
        raceEnrollmentCode = await users.GenerateTwoFactorTokenAsync(
            user!, users.Options.Tokens.AuthenticatorTokenProvider);
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceEnrollmentUserId,
        "operational_mfa.enrolled", enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var enrollmentTask = PostWithCsrfAsync(raceEnrollmentClient,
            "/api/v1/auth/mfa/enrollment/verify", raceEnrollmentPostToken, new { code = raceEnrollmentCode });
        try
        {
            await WaitForSecurityAuditDelayAsync(migratorConnectionString);
            var disableTask = Task.Run(async () =>
            {
                await using var scope = raceFactory.Services.CreateAsyncScope();
                var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
                return await security.DisableAsync(raceEnrollmentUserId);
            });
            await WaitForIdentityRowLockAsync(migratorConnectionString);
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await enrollmentTask;
            Ensure(response.StatusCode == HttpStatusCode.OK,
                "MFA enrollment completes while concurrent account disablement waits on the identity lock.");
            Ensure(await disableTask,
                "Account disablement commits immediately after the in-flight MFA enrollment transaction.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceEnrollmentUserId,
        "operational_mfa.enrolled", enabled: false);
    Ensure((await raceEnrollmentClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "An application cookie emitted by MFA enrollment racing with account disablement is unusable.");

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
           suspendedTenantAudit[0].ActorId is null && !suspendedTenantAudit[0].HasIdentityTarget,
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

    if (string.Equals(Environment.GetEnvironmentVariable("ATENDEE_RUN_BROWSER_E2E"), "true", StringComparison.Ordinal))
    {
        await BrowserE2eRunner.RunAsync(
            identityConnectionString,
            tenantConnectionString,
            migratorConnectionString,
            apiContentRoot,
            Path.Combine(repositoryRoot, "web", "atendee-portal"));
    }
}
finally
{
    factory.Dispose();
    mfaFactory?.Dispose();
    promotionFactory?.Dispose();
    roleMatrixFactory?.Dispose();
    statusFactory?.Dispose();
    try
    {
        if (statusKeyRing is not null && Directory.Exists(statusKeyRing))
            Directory.Delete(statusKeyRing, recursive: true);
        if (lockoutKeyRing is not null && Directory.Exists(lockoutKeyRing))
            Directory.Delete(lockoutKeyRing, recursive: true);
        if (sequentialLockoutKeyRing is not null && Directory.Exists(sequentialLockoutKeyRing))
            Directory.Delete(sequentialLockoutKeyRing, recursive: true);
        if (crossFlowKeyRing is not null && Directory.Exists(crossFlowKeyRing))
            Directory.Delete(crossFlowKeyRing, recursive: true);
        if (revocationKeyRing is not null && Directory.Exists(revocationKeyRing))
            Directory.Delete(revocationKeyRing, recursive: true);
        if (mfaKeyRing is not null && Directory.Exists(mfaKeyRing))
            Directory.Delete(mfaKeyRing, recursive: true);
        if (promotionKeyRing is not null && Directory.Exists(promotionKeyRing))
            Directory.Delete(promotionKeyRing, recursive: true);
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

static async Task<HttpResponseMessage> PutWithCsrfAsync(
    HttpClient client,
    string path,
    string token,
    object body)
{
    using var request = new HttpRequestMessage(HttpMethod.Put, path)
    {
        Content = JsonContent.Create(body)
    };
    request.Headers.Add("X-CSRF-TOKEN", token);
    return await client.SendAsync(request);
}

static async Task<string> GetAntiforgeryRequestTokenAsync(HttpClient client)
{
    using var response = await client.GetAsync("/api/v1/auth/antiforgery");
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    var token = body.GetProperty("requestToken").GetString();
    return !string.IsNullOrWhiteSpace(token)
        ? token
        : throw new InvalidOperationException("Antiforgery request token was not issued.");
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

static async Task<bool> ServiceIsHiddenFromTenantAsync(
    string connectionString,
    Guid foreignTenantId,
    Guid owningTenantId,
    Guid serviceId)
{
    if (foreignTenantId == owningTenantId)
        return false;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_runtime", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using (var tenant = new NpgsqlCommand(
        "SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction))
    {
        tenant.Parameters.AddWithValue("tenant", foreignTenantId.ToString("D"));
        await tenant.ExecuteScalarAsync();
    }

    await using var query = new NpgsqlCommand(
        "SELECT count(*) FROM atendee.services WHERE service_id = @service",
        connection, transaction);
    query.Parameters.AddWithValue("service", serviceId);
    var visible = Convert.ToInt32(await query.ExecuteScalarAsync());
    await transaction.CommitAsync();
    return visible == 0;
}

static async Task<string[]> ReadProfessionalAuditAsync(
    string connectionString,
    Guid tenantId,
    Guid professionalId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        "SELECT event_type FROM atendee.tenant_audit_events WHERE tenant_id = @tenant AND resource_id = @professional ORDER BY occurred_at_utc, event_id",
        connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    command.Parameters.AddWithValue("professional", professionalId);
    var events = new List<string>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            events.Add(reader.GetString(0));
    }
    await transaction.CommitAsync();
    return events.ToArray();
}

static async Task<string[]> ReadServiceAuditAsync(
    string connectionString,
    Guid tenantId,
    Guid serviceId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        "SELECT event_type FROM atendee.tenant_audit_events WHERE tenant_id = @tenant AND resource_id = @service ORDER BY occurred_at_utc, event_id",
        connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    command.Parameters.AddWithValue("service", serviceId);
    var events = new List<string>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            events.Add(reader.GetString(0));
    }

    await transaction.CommitAsync();
    return events.ToArray();
}

static async Task<(short From, short To, Guid? ActorId, bool HasIdentityTarget)[]> ReadTenantStatusAuditAsync(
    string connectionString,
    Guid tenantId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using var command = new NpgsqlCommand(
        @"SELECT ""FromTenantStatus"", ""ToTenantStatus"", ""ActorIdentityUserId"", ""TargetIdentityUserId"" IS NOT NULL FROM identity.""SecurityAuditEvents"" WHERE ""TargetTenantId"" = @tenant AND ""EventType"" = 'tenant_status.changed' ORDER BY ""OccurredAtUtc"", ""Id"" ", connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    var values = new List<(short From, short To, Guid? ActorId, bool HasIdentityTarget)>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            values.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetBoolean(3)));
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

static async Task<bool> AuthLoginCannotReadTenantsAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    try
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM atendee.tenants LIMIT 1", connection);
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

static async Task SetPendingMfaAuditFailureTriggerAsync(string connectionString, bool enabled)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand(
        "SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    if (enabled)
    {
        await using (var createFunction = new NpgsqlCommand(
            """
            CREATE OR REPLACE FUNCTION identity.reject_pending_mfa_audit_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."EventType" = 'operational_auth.password_verified_mfa_pending' THEN
                    RAISE EXCEPTION 'Forced pending-MFA audit failure for HTTP integration test.';
                END IF;
                RETURN NEW;
            END;
            $function$;
            """, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectPendingMfaAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestRejectPendingMfaAudit"
                BEFORE INSERT ON identity."SecurityAuditEvents"
                FOR EACH ROW EXECUTE FUNCTION identity.reject_pending_mfa_audit_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectPendingMfaAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.reject_pending_mfa_audit_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task SetIdentityUpdateDelayTriggerAsync(
    string connectionString,
    Guid targetUserId,
    bool enabled)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand(
        "SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    if (enabled)
    {
        var createFunctionSql = $"""
            CREATE OR REPLACE FUNCTION identity.delay_identity_update_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."Id" = '{targetUserId:D}'::uuid
                   AND NEW."AccessFailedCount" IS DISTINCT FROM OLD."AccessFailedCount" THEN
                    PERFORM pg_advisory_xact_lock(846271903514::bigint);
                END IF;
                RETURN NEW;
            END;
            $function$;
            """;
        await using (var createFunction = new NpgsqlCommand(createFunctionSql, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelayIdentityUpdate\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestDelayIdentityUpdate"
                BEFORE UPDATE OF "AccessFailedCount" ON identity."AspNetUsers"
                FOR EACH ROW EXECUTE FUNCTION identity.delay_identity_update_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelayIdentityUpdate\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.delay_identity_update_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task WaitForIdentityUpdateDelayAsync(string connectionString)
{
    await WaitForPostgresActivityAsync(connectionString,
        """
        SELECT EXISTS (
            SELECT 1
              FROM pg_stat_activity
             WHERE wait_event_type = 'Lock'
               AND wait_event = 'advisory'
               AND query ILIKE '%AspNetUsers%'
               AND query ILIKE '%UPDATE%'
        )
        """,
        "The password-verification request did not reach the test-only Identity update advisory lock.");
}

static async Task SetSecurityAuditDelayTriggerAsync(
    string connectionString,
    Guid targetUserId,
    string eventType,
    bool enabled)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand(
        "SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    if (enabled)
    {
        var createFunctionSql = $"""
            CREATE OR REPLACE FUNCTION identity.delay_security_audit_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."TargetIdentityUserId" = '{targetUserId:D}'::uuid
                   AND NEW."EventType" = '{eventType}' THEN
                    PERFORM pg_advisory_xact_lock(846271903514::bigint);
                END IF;
                RETURN NEW;
            END;
            $function$;
            """;
        await using (var createFunction = new NpgsqlCommand(createFunctionSql, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelaySecurityAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestDelaySecurityAudit"
                BEFORE INSERT ON identity."SecurityAuditEvents"
                FOR EACH ROW EXECUTE FUNCTION identity.delay_security_audit_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelaySecurityAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.delay_security_audit_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task<NpgsqlConnection> AcquireSecurityAuditDelayLockAsync(string connectionString)
{
    var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        "SELECT pg_advisory_lock(846271903514::bigint)", connection);
    await command.ExecuteNonQueryAsync();
    return connection;
}

static async Task ReleaseSecurityAuditDelayLockAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        "SELECT pg_advisory_unlock(846271903514::bigint)", connection);
    await command.ExecuteScalarAsync();
}

static async Task WaitForSecurityAuditDelayAsync(string connectionString)
{
    await WaitForPostgresActivityAsync(connectionString,
        """
        SELECT EXISTS (
            SELECT 1
              FROM pg_stat_activity
             WHERE wait_event_type = 'Lock'
               AND wait_event = 'advisory'
               AND query ILIKE '%SecurityAuditEvents%'
        )
        """,
        "The HTTP request did not reach the test-only security-audit advisory lock.");
}

static async Task WaitForIdentityRowLockAsync(string connectionString)
{
    await WaitForPostgresActivityAsync(connectionString,
        """
        SELECT EXISTS (
            SELECT 1
              FROM pg_stat_activity
             WHERE wait_event_type = 'Lock'
               AND query ILIKE '%AspNetUsers%'
               AND query ILIKE '%FOR UPDATE%'
        )
        """,
        "The concurrent account-security operation did not wait for the in-flight identity row lock.");
}

static async Task WaitForPostgresActivityAsync(
    string connectionString,
    string query,
    string failureMessage)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
        await using var command = new NpgsqlCommand(query, connection);
        if (await command.ExecuteScalarAsync() is true)
            return;

        await Task.Delay(TimeSpan.FromMilliseconds(25));
    }

    throw new InvalidOperationException(failureMessage);
}

static async Task SetLoginFailureDelayTriggerAsync(
    string connectionString,
    Guid targetUserId,
    bool enabled)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand(
        "SET LOCAL ROLE atendee_identity_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    if (enabled)
    {
        var createFunctionSql = $"""
            CREATE OR REPLACE FUNCTION identity.delay_login_failure_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."Id" = '{targetUserId:D}'::uuid
                   AND NEW."AccessFailedCount" IS DISTINCT FROM OLD."AccessFailedCount" THEN
                    PERFORM pg_sleep(1.0);
                END IF;
                RETURN NEW;
            END;
            $function$;
            """;
        await using (var createFunction = new NpgsqlCommand(createFunctionSql, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelayLoginFailure\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestDelayLoginFailure"
                BEFORE UPDATE OF "AccessFailedCount" ON identity."AspNetUsers"
                FOR EACH ROW EXECUTE FUNCTION identity.delay_login_failure_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestDelayLoginFailure\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.delay_login_failure_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
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
