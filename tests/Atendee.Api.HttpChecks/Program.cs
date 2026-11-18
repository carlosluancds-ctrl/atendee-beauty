using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    var login = await PostWithCsrfAsync(client, "/api/v1/auth/login", requestToken!, new
    {
        email,
        password
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
    Ensure(auditTypes.Length == 2 &&
           auditTypes.Contains("operational_account.sessions_revoked") &&
           auditTypes.Contains("operational_account.disabled"),
        "Session revocation and account disablement are durably recorded.");

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

    await SeedMembershipAsync(migratorConnectionString, ownerTenantId, ownerUserId, roleId: 1);

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
    Ensure(true, "Valid TOTP completes owner MFA enrollment.");
    var recoveryCodes = (await enrollmentVerify.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("recoveryCodes").EnumerateArray()
        .Select(value => value.GetString()!).ToArray();
    Ensure(recoveryCodes.Length == 10 && recoveryCodes.All(code => !string.IsNullOrWhiteSpace(code)),
        "MFA enrollment returns ten one-time recovery codes.");

    var ownerMe = await mfaClient.GetAsync("/api/v1/auth/me");
    Ensure(ownerMe.StatusCode == HttpStatusCode.OK,
        "Owner receives an authenticated application session only after MFA enrollment.");
    var ownerMeBody = await ownerMe.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerMeBody.GetProperty("role").GetString() == TenantRole.Owner.ToString(),
        "Owner session carries the persisted owner membership role.");

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

    await UpdateMembershipAsync(migratorConnectionString, promotionUserId, roleId: 1, isActive: true);
    Ensure((await promotionClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Forbidden,
        "Promoting a password-only session to Owner does not grant privileged access.");

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
    Ensure((await deactivationClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "Deactivating membership invalidates the existing tenant session on its next request.");

    Console.WriteLine("RESULT: HTTP authentication, antiforgery, cookies, session revocation, logout, MFA and live membership checks passed.");
}
finally
{
    factory.Dispose();
    mfaFactory?.Dispose();
    try
    {
        if (mfaKeyRing is not null && Directory.Exists(mfaKeyRing))
            Directory.Delete(mfaKeyRing, recursive: true);
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

static async Task SeedMembershipAsync(string connectionString, Guid tenantId, Guid userId, int roleId = 3)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
        await role.ExecuteNonQueryAsync();

    await using (var tenant = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenants(id,name,slug,time_zone_id,status,created_at_utc)
        VALUES(@id,'HTTP Synthetic Salon','http-' || @id::text,'America/Sao_Paulo',3,now())
        """, connection, transaction))
    {
        tenant.Parameters.AddWithValue("id", tenantId);
        await tenant.ExecuteNonQueryAsync();
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
