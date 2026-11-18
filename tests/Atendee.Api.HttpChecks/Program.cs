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

var factory = new WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>().WithWebHostBuilder(web =>
{
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

    Console.WriteLine("RESULT: HTTP authentication, antiforgery, cookies and session-revocation checks passed.");
}
finally
{
    factory.Dispose();
    try
    {
        if (Directory.Exists(keyRing))
            Directory.Delete(keyRing, recursive: true);
    }
    catch
    {
        // Temporary test key cleanup must not hide the security-test result.
    }
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

static async Task SeedMembershipAsync(string connectionString, Guid tenantId, Guid userId)
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
        VALUES(@tenant,@user,'HTTP Synthetic Receptionist',3,true,now())
        """, connection, transaction))
    {
        membership.Parameters.AddWithValue("tenant", tenantId);
        membership.Parameters.AddWithValue("user", userId);
        await membership.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}
