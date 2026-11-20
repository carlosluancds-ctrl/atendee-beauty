using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Atendee.Application.Abstractions;
using Atendee.Api.Authentication;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

var factory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    var certificateTestDirectory = Path.Combine(
        Path.GetTempPath(), "atendee-dp-certificate-" + Guid.NewGuid().ToString("N"));
    var certificateKeyRing = Path.Combine(certificateTestDirectory, "key-ring");
    Directory.CreateDirectory(certificateTestDirectory);
    var certificatePath = Path.Combine(certificateTestDirectory, "protector.pfx");
    const string certificatePassword = "Atendee-DP-CI-Only-2026!";
    var previousKeyRingPath = Environment.GetEnvironmentVariable("DataProtection__KeyRingPath");
    var previousCertificatePath = Environment.GetEnvironmentVariable("DataProtection__CertificatePath");
    var previousCertificatePassword = Environment.GetEnvironmentVariable("DataProtection__CertificatePassword");
    try
    {
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest(
            "CN=Atendee Data Protection Integration Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var generatedCertificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(2));
        await File.WriteAllBytesAsync(
            certificatePath, generatedCertificate.Export(X509ContentType.Pfx, certificatePassword));

        Environment.SetEnvironmentVariable("DataProtection__KeyRingPath", certificateKeyRing);
        Environment.SetEnvironmentVariable("DataProtection__CertificatePath", certificatePath);
        Environment.SetEnvironmentVariable("DataProtection__CertificatePassword", certificatePassword);

        string protectedPayload;
        using (var certificateFactory = CreateIsolatedRateLimitFactory()
            .WithWebHostBuilder(web =>
            {
                web.UseContentRoot(apiContentRoot);
                web.UseEnvironment("Production");
            }))
        {
            var protector = certificateFactory.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Atendee.Api.HttpChecks.DataProtection");
            protectedPayload = protector.Protect("persistent-certificate-protection");
        }

        var keyFiles = Directory.GetFiles(certificateKeyRing, "*.xml");
        Ensure(keyFiles.Length > 0 &&
               File.ReadAllText(keyFiles[0]).Contains("encryptedSecret", StringComparison.OrdinalIgnoreCase),
            "Production Data Protection persists certificate-encrypted keys instead of plaintext key material.");

        using (var restartedCertificateFactory = CreateIsolatedRateLimitFactory()
            .WithWebHostBuilder(web =>
            {
                web.UseContentRoot(apiContentRoot);
                web.UseEnvironment("Production");
            }))
        {
            var restartedProtector = restartedCertificateFactory.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Atendee.Api.HttpChecks.DataProtection");
            Ensure(restartedProtector.Unprotect(protectedPayload) == "persistent-certificate-protection",
                "A restarted production host can unprotect data using the persistent certificate-protected key ring.");
        }

        Environment.SetEnvironmentVariable("DataProtection__CertificatePassword", "incorrect-password");
        var invalidCertificateRejected = false;
        using (var invalidCertificateFactory = CreateIsolatedRateLimitFactory()
            .WithWebHostBuilder(web =>
            {
                web.UseContentRoot(apiContentRoot);
                web.UseEnvironment("Production");
            }))
        {
            try
            {
                _ = invalidCertificateFactory.Services;
            }
            catch (Exception exception) when (
                exception.ToString().Contains("Data Protection certificate could not be loaded", StringComparison.Ordinal))
            {
                invalidCertificateRejected = true;
            }
        }

        Ensure(invalidCertificateRejected,
            "An unreadable or incorrectly password-protected Data Protection certificate fails production startup closed.");

        if (!OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable("DataProtection__CertificatePath", null);
            Environment.SetEnvironmentVariable("DataProtection__CertificatePassword", null);
            var missingCertificateRejected = false;
            using var missingCertificateFactory = CreateIsolatedRateLimitFactory()
                .WithWebHostBuilder(web =>
                {
                    web.UseContentRoot(apiContentRoot);
                    web.UseEnvironment("Production");
                });
            try
            {
                _ = missingCertificateFactory.Services;
            }
            catch (Exception exception) when (
                exception.ToString().Contains("Non-Windows production requires DataProtection:CertificatePath", StringComparison.Ordinal))
            {
                missingCertificateRejected = true;
            }

            Ensure(missingCertificateRejected,
                "Non-Windows production fails startup closed when no explicit Data Protection certificate is configured.");
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("DataProtection__KeyRingPath", previousKeyRingPath);
        Environment.SetEnvironmentVariable("DataProtection__CertificatePath", previousCertificatePath);
        Environment.SetEnvironmentVariable("DataProtection__CertificatePassword", previousCertificatePassword);
        if (Directory.Exists(certificateTestDirectory))
        {
            Directory.Delete(certificateTestDirectory, recursive: true);
        }
    }

    var defaultForwardedHeaders = factory.Services
        .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    Ensure(defaultForwardedHeaders.ForwardedHeaders ==
               (ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto) &&
           defaultForwardedHeaders.ForwardLimit == 1 &&
           defaultForwardedHeaders.RequireHeaderSymmetry &&
           defaultForwardedHeaders.KnownProxies.Count == 2 &&
           defaultForwardedHeaders.KnownProxies.Contains(IPAddress.Any) &&
           defaultForwardedHeaders.KnownProxies.Contains(IPAddress.IPv6Any) &&
           defaultForwardedHeaders.KnownIPNetworks.Count == 0,
        "Forwarded headers use an explicit deny-all proxy sentinel by default and accept only one symmetric X-Forwarded-For/X-Forwarded-Proto hop.");

    using var forwardedHeadersLoggerFactory = LoggerFactory.Create(_ => { });
    var spoofedForwardedContext = new DefaultHttpContext();
    spoofedForwardedContext.Connection.RemoteIpAddress = IPAddress.Loopback;
    spoofedForwardedContext.Request.Scheme = "http";
    spoofedForwardedContext.Request.Host = new HostString("api.example.invalid");
    spoofedForwardedContext.Request.Headers["X-Forwarded-For"] = "203.0.113.42";
    spoofedForwardedContext.Request.Headers["X-Forwarded-Proto"] = "https";
    spoofedForwardedContext.Request.Headers["X-Forwarded-Host"] = "attacker.example";
    var untrustedForwardedMiddleware = new ForwardedHeadersMiddleware(
        _ => Task.CompletedTask,
        forwardedHeadersLoggerFactory,
        Options.Create(defaultForwardedHeaders));
    await untrustedForwardedMiddleware.Invoke(spoofedForwardedContext);
    Ensure(spoofedForwardedContext.Connection.RemoteIpAddress == IPAddress.Loopback &&
           spoofedForwardedContext.Request.Scheme == "http" &&
           spoofedForwardedContext.Request.Host == new HostString("api.example.invalid"),
        "Untrusted forwarded IP, protocol and host headers cannot spoof the request origin or rate-limit partition.");

    const string trustedProxyEnvironmentKey = "ForwardedHeaders__KnownProxies__0";
    var trustedProxyAddressCases = new (string ConfiguredAddress, IPAddress ExpectedAddress)[]
    {
        ("::1", IPAddress.IPv6Loopback),
        ("127.0.0.1", IPAddress.Loopback),
        ("::ffff:127.0.0.1", IPAddress.Loopback)
    };
    ForwardedHeadersOptions trustedForwardedHeaders = new();
    foreach (var trustedProxyAddressCase in trustedProxyAddressCases)
    {
        var previousTrustedProxyEnvironmentValue =
            Environment.GetEnvironmentVariable(trustedProxyEnvironmentKey);
        try
        {
            // Program.cs reads the proxy allowlist before WebApplicationFactory's
            // ConfigureAppConfiguration callback runs. Supply this startup setting
            // through the environment provider so the application sees it early.
            Environment.SetEnvironmentVariable(
                trustedProxyEnvironmentKey, trustedProxyAddressCase.ConfiguredAddress);
            using var trustedProxyFactory = CreateIsolatedRateLimitFactory()
                .WithWebHostBuilder(web =>
                {
                    web.UseContentRoot(apiContentRoot);
                    web.UseEnvironment("Development");
                    web.ConfigureAppConfiguration((_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Identity"] = null,
                            ["ConnectionStrings:Tenant"] = null,
                            ["DataProtection:KeyRingPath"] = null
                        });
                    });
                });
            trustedForwardedHeaders = trustedProxyFactory.Services
                .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                trustedProxyEnvironmentKey, previousTrustedProxyEnvironmentValue);
        }

        Ensure(trustedForwardedHeaders.KnownProxies.Contains(trustedProxyAddressCase.ExpectedAddress) &&
               trustedForwardedHeaders.ForwardLimit == 1,
            $"Explicit proxy address '{trustedProxyAddressCase.ConfiguredAddress}' is trusted and canonicalized for one forwarding hop.");
    }

    var trustedForwardedContext = new DefaultHttpContext();
    trustedForwardedContext.Connection.RemoteIpAddress = IPAddress.Loopback;
    trustedForwardedContext.Request.Scheme = "http";
    trustedForwardedContext.Request.Host = new HostString("api.example.invalid");
    trustedForwardedContext.Request.Headers["X-Forwarded-For"] = "203.0.113.42";
    trustedForwardedContext.Request.Headers["X-Forwarded-Proto"] = "https";
    trustedForwardedContext.Request.Headers["X-Forwarded-Host"] = "attacker.example";
    var trustedForwardedMiddleware = new ForwardedHeadersMiddleware(
        _ => Task.CompletedTask,
        forwardedHeadersLoggerFactory,
        Options.Create(trustedForwardedHeaders));
    await trustedForwardedMiddleware.Invoke(trustedForwardedContext);
    Ensure(IPAddress.Parse("203.0.113.42").Equals(
               trustedForwardedContext.Connection.RemoteIpAddress),
        "A known proxy may supply the client IP.");
    Ensure(trustedForwardedContext.Request.Scheme == "https",
        "A known proxy may supply the HTTPS scheme.");
    Ensure(trustedForwardedContext.Request.Host == new HostString("api.example.invalid"),
        "X-Forwarded-Host is ignored unless explicitly enabled.");

    var invalidProxyAddresses = new[]
    {
        "not-an-ip",
        "0.0.0.0",
        "224.0.0.1",
        "240.0.0.1",
        "255.255.255.255",
        "::",
        "ff02::1",
        "::ffff:0.0.0.0",
        "::ffff:224.0.0.1"
    };
    foreach (var invalidProxyAddress in invalidProxyAddresses)
    {
        var previousInvalidProxyEnvironmentValue =
            Environment.GetEnvironmentVariable(trustedProxyEnvironmentKey);
        var invalidProxyRejected = false;
        try
        {
            Environment.SetEnvironmentVariable(
                trustedProxyEnvironmentKey, invalidProxyAddress);
            using var invalidProxyFactory = CreateIsolatedRateLimitFactory()
                .WithWebHostBuilder(web =>
                {
                    web.UseContentRoot(apiContentRoot);
                    web.UseEnvironment("Development");
                    web.ConfigureAppConfiguration((_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Identity"] = null,
                            ["ConnectionStrings:Tenant"] = null,
                            ["DataProtection:KeyRingPath"] = null
                        });
                    });
                });
            try
            {
                _ = invalidProxyFactory.Services;
            }
            catch (Exception exception) when (
                exception.ToString().Contains("ForwardedHeaders:KnownProxies", StringComparison.Ordinal))
            {
                invalidProxyRejected = true;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                trustedProxyEnvironmentKey, previousInvalidProxyEnvironmentValue);
        }

        Ensure(invalidProxyRejected,
            $"Invalid trusted-proxy address '{invalidProxyAddress}' fails application startup closed.");
    }

    // V89: two independent limiter instances share one atomic PostgreSQL counter.
    var partitionKeySecret = Convert.FromBase64String(
        Required("AuthenticationRateLimiting__PartitionKeySecret"));
    await using var limiterDataSourceA = NpgsqlDataSource.Create(identityConnectionString);
    await using var limiterDataSourceB = NpgsqlDataSource.Create(identityConnectionString);
    using var rateLimitLoggerFactory = LoggerFactory.Create(_ => { });
    var limiterA = new PostgresDistributedAuthenticationRateLimiter(
        limiterDataSourceA,
        new AuthenticationRateLimitPartitionKey(partitionKeySecret),
        rateLimitLoggerFactory.CreateLogger<PostgresDistributedAuthenticationRateLimiter>());
    var limiterB = new PostgresDistributedAuthenticationRateLimiter(
        limiterDataSourceB,
        new AuthenticationRateLimitPartitionKey(partitionKeySecret),
        rateLimitLoggerFactory.CreateLogger<PostgresDistributedAuthenticationRateLimiter>());
    var limiterPolicy = "v89-concurrency-" + Guid.NewGuid().ToString("N");
    var limiterIp = IPAddress.Parse("198.51.100.42");
    var limiterNow = DateTimeOffset.UtcNow;
    const int distributedPermitLimit = 7;
    var concurrentAcquisitions = await Task.WhenAll(
        Enumerable.Range(0, 40).Select(index =>
            (index % 2 == 0 ? limiterA : limiterB).TryAcquireAsync(
                limiterPolicy,
                limiterIp,
                distributedPermitLimit,
                TimeSpan.FromMinutes(1),
                limiterNow)));
    Ensure(concurrentAcquisitions.All(result => result.IsAvailable) &&
           concurrentAcquisitions.Count(result => result.IsAllowed) == distributedPermitLimit &&
           concurrentAcquisitions.Count(result => !result.IsAllowed) == 33,
        "Two independent API limiter instances share an atomic PostgreSQL fixed-window counter without lost increments or excess permits.");

    var limiterWindowTicks = TimeSpan.FromMinutes(1).Ticks;
    var limiterWindowStart = new DateTimeOffset(
        limiterNow.UtcDateTime.Ticks / limiterWindowTicks * limiterWindowTicks,
        TimeSpan.Zero);
    await using (var hashConnection = await limiterDataSourceA.OpenConnectionAsync())
    await using (var hashCommand = new NpgsqlCommand(
        """
        SELECT "PartitionHash", "RequestCount"
          FROM identity."AuthenticationRateLimitBuckets"
         WHERE "Policy" = @policy
           AND "WindowStartedAtUtc" = @windowStart
        """,
        hashConnection))
    {
        hashCommand.Parameters.AddWithValue("policy", limiterPolicy);
        hashCommand.Parameters.AddWithValue("windowStart", limiterWindowStart);
        await using var hashReader = await hashCommand.ExecuteReaderAsync();
        Ensure(await hashReader.ReadAsync(), "The concurrent rate-limit bucket is persisted.");
        var storedPartitionHash = hashReader.GetFieldValue<byte[]>(0);
        var persistedRequestCount = hashReader.GetInt64(1);
        Ensure(storedPartitionHash.Length == 32 && persistedRequestCount == 40 &&
               !storedPartitionHash.SequenceEqual(limiterIp.GetAddressBytes()),
            "The rate-limit table stores a 32-byte keyed IP digest, never the raw client IP address.");
    }

    var secondClientPartition = await limiterB.TryAcquireAsync(
        limiterPolicy,
        IPAddress.Parse("198.51.100.43"),
        distributedPermitLimit,
        TimeSpan.FromMinutes(1),
        limiterNow);
    Ensure(secondClientPartition.IsAvailable && secondClientPartition.IsAllowed,
        "Different client IP addresses receive independent keyed rate-limit partitions.");

    var nextWindowPartition = await limiterB.TryAcquireAsync(
        limiterPolicy,
        limiterIp,
        distributedPermitLimit,
        TimeSpan.FromMinutes(1),
        limiterNow.AddMinutes(1));
    Ensure(nextWindowPartition.IsAvailable && nextWindowPartition.IsAllowed,
        "A new UTC fixed window starts with a fresh shared counter.");

    var mappedIpPolicy = "v89-mapped-ip-" + Guid.NewGuid().ToString("N");
    var mappedIpv4Decision = await limiterA.TryAcquireAsync(
        mappedIpPolicy, IPAddress.Parse("198.51.100.44"), 1, TimeSpan.FromMinutes(1), limiterNow);
    var mappedIpv6Decision = await limiterB.TryAcquireAsync(
        mappedIpPolicy, IPAddress.Parse("::ffff:198.51.100.44"), 1, TimeSpan.FromMinutes(1), limiterNow);
    Ensure(mappedIpv4Decision.IsAllowed && !mappedIpv6Decision.IsAllowed,
        "IPv4-mapped IPv6 addresses normalize to the same rate-limit partition as their IPv4 address.");

    await using (var privacyConnection = await limiterDataSourceA.OpenConnectionAsync())
    await using (var privacyCommand = new NpgsqlCommand(
        """
        SELECT count(*)
          FROM information_schema.columns
         WHERE table_schema = 'identity'
           AND table_name = 'AuthenticationRateLimitBuckets'
           AND column_name IN ('IpAddress', 'ClientIp', 'RemoteIpAddress')
        """,
        privacyConnection))
    {
        Ensure(Convert.ToInt32(await privacyCommand.ExecuteScalarAsync()) == 0,
            "The shared rate-limit schema contains no plaintext IP address column.");
    }

    var stalePolicy = "v89-stale-" + Guid.NewGuid().ToString("N");
    await using (var staleInsertConnection = await limiterDataSourceA.OpenConnectionAsync())
    await using (var staleInsert = new NpgsqlCommand(
        """
        INSERT INTO identity."AuthenticationRateLimitBuckets"
            ("Policy", "PartitionHash", "WindowStartedAtUtc", "RequestCount", "ExpiresAtUtc")
        VALUES (@policy, @hash, @windowStart, 1, @expiresAt)
        """,
        staleInsertConnection))
    {
        staleInsert.Parameters.AddWithValue("policy", stalePolicy);
        staleInsert.Parameters.AddWithValue("hash", RandomNumberGenerator.GetBytes(32));
        staleInsert.Parameters.AddWithValue("windowStart", limiterNow.AddMinutes(-20));
        staleInsert.Parameters.AddWithValue("expiresAt", limiterNow.AddMinutes(-10));
        await staleInsert.ExecuteNonQueryAsync();
    }

    var cleanupResult = await limiterA.TryAcquireAsync(
        "v89-cleanup-trigger-" + Guid.NewGuid().ToString("N"),
        limiterIp,
        10,
        TimeSpan.FromMinutes(1),
        limiterNow);
    await using (var staleCountConnection = await limiterDataSourceA.OpenConnectionAsync())
    await using (var staleCount = new NpgsqlCommand(
        """
        SELECT count(*)
          FROM identity."AuthenticationRateLimitBuckets"
         WHERE "Policy" = @policy
        """,
        staleCountConnection))
    {
        staleCount.Parameters.AddWithValue("policy", stalePolicy);
        Ensure(cleanupResult.IsAvailable && Convert.ToInt64(await staleCount.ExecuteScalarAsync()) == 0,
            "Expired rate-limit buckets are opportunistically purged in bounded batches.");
    }

    var unavailableConnectionString = new NpgsqlConnectionStringBuilder(identityConnectionString)
    {
        Host = "127.0.0.1",
        Port = 1,
        Timeout = 1,
        CommandTimeout = 1,
        Pooling = false
    }.ConnectionString;
    await using var unavailableDataSource = NpgsqlDataSource.Create(unavailableConnectionString);
    var unavailableLimiter = new PostgresDistributedAuthenticationRateLimiter(
        unavailableDataSource,
        new AuthenticationRateLimitPartitionKey(partitionKeySecret),
        rateLimitLoggerFactory.CreateLogger<PostgresDistributedAuthenticationRateLimiter>());
    var unavailableDecision = await unavailableLimiter.TryAcquireAsync(
        "v89-backend-outage",
        limiterIp,
        5,
        TimeSpan.FromMinutes(1),
        limiterNow);
    Ensure(!unavailableDecision.IsAvailable && !unavailableDecision.IsAllowed,
        "A PostgreSQL outage produces an unavailable/deny decision rather than a local in-memory fallback.");

    var failClosedContext = new DefaultHttpContext();
    failClosedContext.Connection.RemoteIpAddress = limiterIp;
    failClosedContext.SetEndpoint(new Endpoint(
        _ => Task.CompletedTask,
        new EndpointMetadataCollection(
            new DistributedAuthenticationRateLimitMetadata(
                "v89-backend-outage", 5, TimeSpan.FromMinutes(1))),
        "V89 fail-closed middleware check"));
    using var failClosedServices = new ServiceCollection()
        .AddSingleton<IDistributedAuthenticationRateLimiter>(unavailableLimiter)
        .BuildServiceProvider();
    failClosedContext.RequestServices = failClosedServices;
    var downstreamWasInvoked = false;
    var failClosedMiddleware = new DistributedAuthenticationRateLimitingMiddleware(
        _ =>
        {
            downstreamWasInvoked = true;
            return Task.CompletedTask;
        },
        rateLimitLoggerFactory.CreateLogger<DistributedAuthenticationRateLimitingMiddleware>());
    await failClosedMiddleware.InvokeAsync(failClosedContext);
    Ensure(failClosedContext.Response.StatusCode == StatusCodes.Status503ServiceUnavailable &&
           failClosedContext.Response.Headers["Retry-After"] == "5" &&
           !downstreamWasInvoked,
        "The HTTP middleware returns 503 with Retry-After and never executes protected endpoints when the shared store is unavailable.");

    var limitedContext = new DefaultHttpContext();
    limitedContext.Connection.RemoteIpAddress = limiterIp;
    limitedContext.SetEndpoint(new Endpoint(
        _ => Task.CompletedTask,
        new EndpointMetadataCollection(
            new DistributedAuthenticationRateLimitMetadata(
                limiterPolicy, distributedPermitLimit, TimeSpan.FromMinutes(1))),
        "V89 exhausted-window middleware check"));
    using var limitedServices = new ServiceCollection()
        .AddSingleton<IDistributedAuthenticationRateLimiter>(limiterA)
        .BuildServiceProvider();
    limitedContext.RequestServices = limitedServices;
    var limitedDownstreamWasInvoked = false;
    var limitedMiddleware = new DistributedAuthenticationRateLimitingMiddleware(
        _ =>
        {
            limitedDownstreamWasInvoked = true;
            return Task.CompletedTask;
        },
        rateLimitLoggerFactory.CreateLogger<DistributedAuthenticationRateLimitingMiddleware>());
    await limitedMiddleware.InvokeAsync(limitedContext);
    Ensure(limitedContext.Response.StatusCode == StatusCodes.Status429TooManyRequests &&
           !string.IsNullOrWhiteSpace(limitedContext.Response.Headers["Retry-After"]) &&
           !limitedDownstreamWasInvoked,
        "An exhausted distributed window returns HTTP 429 with Retry-After before invoking the protected endpoint.");
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
    var receptionistCustomerCreate = await PostWithCsrfAsync(client, "/api/v1/customers", receptionistRequestToken, new
    {
        fullName = "  Cliente Recepção  ",
        phoneNumber = "+55 (11) 98888-7777",
        emailAddress = (string?)null
    });
    Ensure(receptionistCustomerCreate.StatusCode == HttpStatusCode.Created,
        "Receptionist can create a customer within the active tenant.");
    var invalidCustomerCreate = await PostWithCsrfAsync(client, "/api/v1/customers", receptionistRequestToken, new
    {
        fullName = "Invalid Customer",
        phoneNumber = (string?)null,
        emailAddress = (string?)null
    });
    Ensure(invalidCustomerCreate.StatusCode == HttpStatusCode.BadRequest,
        "Customer API rejects records without at least one contact method.");
    var receptionistCustomer = await receptionistCustomerCreate.Content.ReadFromJsonAsync<JsonElement>();
    var receptionistCustomerId = receptionistCustomer.GetProperty("id").GetGuid();
    Ensure(receptionistCustomer.GetProperty("fullName").GetString() == "Cliente Recepção" &&
           receptionistCustomer.GetProperty("normalizedPhoneNumber").GetString() == "+5511988887777",
        "Customer API trims names and stores phone numbers in E.164 form.");
    var receptionistCustomerList = await client.GetAsync("/api/v1/customers");
    var receptionistCustomerListBody = await receptionistCustomerList.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(receptionistCustomerList.StatusCode == HttpStatusCode.OK &&
           receptionistCustomerListBody.GetProperty("totalCount").GetInt32() == 1 &&
           receptionistCustomerListBody.GetProperty("page").GetInt32() == 1 &&
           receptionistCustomerListBody.GetProperty("pageSize").GetInt32() == 50 &&
           receptionistCustomerListBody.GetProperty("items").GetArrayLength() == 1 &&
           receptionistCustomerListBody.GetProperty("items")[0].GetProperty("id").GetGuid() == receptionistCustomerId,
        "Receptionist can list only the current salon's customers.");
    var invalidCustomerPage = await client.GetAsync("/api/v1/customers?page=0");
    var invalidCustomerPageSize = await client.GetAsync("/api/v1/customers?pageSize=101");
    Ensure(invalidCustomerPage.StatusCode == HttpStatusCode.BadRequest &&
           invalidCustomerPageSize.StatusCode == HttpStatusCode.BadRequest,
        "Customer list enforces bounded pagination and rejects invalid page sizes.");
    var receptionistArchiveCustomer = await PostWithCsrfAsync(
        client, $"/api/v1/customers/{receptionistCustomerId}/archive", receptionistRequestToken, new { });
    Ensure(receptionistArchiveCustomer.StatusCode == HttpStatusCode.Forbidden,
        "Receptionist cannot archive customer records; archive is restricted to Owner and Manager.");

    var receptionistBusinessHours = await client.GetAsync("/api/v1/business-hours");
    var receptionistBusinessHoursBody = await receptionistBusinessHours.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(receptionistBusinessHours.StatusCode == HttpStatusCode.OK &&
           receptionistBusinessHoursBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
           receptionistBusinessHoursBody.GetProperty("intervals").GetArrayLength() == 0,
        "Receptionist can read the tenant's initially closed weekly schedule and configured time zone.");
    using (var receptionistBusinessHoursPut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/business-hours"))
    {
        receptionistBusinessHoursPut.Headers.Add("X-CSRF-TOKEN", receptionistRequestToken);
        receptionistBusinessHoursPut.Content = JsonContent.Create(new
        {
            intervals = new[] { new { isoDayOfWeek = 1, startTime = "09:00", endTime = "17:00" } }
        });
        using var receptionistBusinessHoursResponse = await client.SendAsync(receptionistBusinessHoursPut);
        Ensure(receptionistBusinessHoursResponse.StatusCode == HttpStatusCode.Forbidden,
            "Receptionist can read but cannot change salon business hours.");
    }
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
    const string mfaTestClientIp = "198.51.100.200";
    const string knownProxy0EnvironmentKey = "ForwardedHeaders__KnownProxies__0";
    const string knownProxy1EnvironmentKey = "ForwardedHeaders__KnownProxies__1";
    var previousKnownProxy0 = Environment.GetEnvironmentVariable(knownProxy0EnvironmentKey);
    var previousKnownProxy1 = Environment.GetEnvironmentVariable(knownProxy1EnvironmentKey);
    string? configuredMfaPartitionSecret;
    try
    {
        // TestServer connects over loopback. Trust only those exact test proxy
        // addresses so this scenario can use a deterministic, isolated client IP.
        Environment.SetEnvironmentVariable(knownProxy0EnvironmentKey, "127.0.0.1");
        Environment.SetEnvironmentVariable(knownProxy1EnvironmentKey, "::1");
        mfaFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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

        configuredMfaPartitionSecret = mfaFactory.Services
            .GetRequiredService<IConfiguration>()["AuthenticationRateLimiting:PartitionKeySecret"];
    }
    finally
    {
        Environment.SetEnvironmentVariable(knownProxy0EnvironmentKey, previousKnownProxy0);
        Environment.SetEnvironmentVariable(knownProxy1EnvironmentKey, previousKnownProxy1);
    }

    Ensure(!string.IsNullOrWhiteSpace(configuredMfaPartitionSecret) &&
           !string.Equals(
               configuredMfaPartitionSecret,
               Environment.GetEnvironmentVariable("AuthenticationRateLimiting__PartitionKeySecret"),
               StringComparison.Ordinal),
        "The MFA integration host uses an isolated rate-limit partition key.");
    using var mfaClient = mfaFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    mfaClient.DefaultRequestHeaders.Add("X-Forwarded-For", mfaTestClientIp);
    mfaClient.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

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

    var mfaTestPartitionKey = Convert.FromBase64String(configuredMfaPartitionSecret!);
    var mfaTestPartitionHash = HMACSHA256.HashData(
        mfaTestPartitionKey,
        new byte[] { 4, 198, 51, 100, 200 });
    await using (var mfaPartitionConnection = await limiterDataSourceA.OpenConnectionAsync())
    await using (var mfaPartitionCommand = new NpgsqlCommand(
        """
        SELECT
            COALESCE(MAX("RequestCount") FILTER (WHERE "Policy" = 'auth-antiforgery'), 0),
            COALESCE(MAX("RequestCount") FILTER (WHERE "Policy" = 'auth-login'), 0)
          FROM identity."AuthenticationRateLimitBuckets"
         WHERE "PartitionHash" = @partitionHash
        """,
        mfaPartitionConnection))
    {
        mfaPartitionCommand.Parameters.AddWithValue("partitionHash", mfaTestPartitionHash);
        await using var mfaPartitionReader = await mfaPartitionCommand.ExecuteReaderAsync();
        Ensure(await mfaPartitionReader.ReadAsync() &&
               mfaPartitionReader.GetInt64(0) == 1 &&
               mfaPartitionReader.GetInt64(1) == 0,
            "Trusted forwarded headers map the MFA test host to its unique client-IP digest, with an unused login quota.");
    }
    var ownerLogin = await PostWithCsrfAsync(mfaClient, "/api/v1/auth/login", mfaRequestToken!, new
    {
        email = ownerEmail,
        password = ownerPassword
    });
    var ownerLoginBodyText = await ownerLogin.Content.ReadAsStringAsync();
    using var ownerLoginBodyDocument = JsonDocument.Parse(ownerLoginBodyText);
    var ownerLoginBody = ownerLoginBodyDocument.RootElement;
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

    var ownerCustomerCreate = await PostWithCsrfAsync(mfaClient, "/api/v1/customers", ownerAuthenticatedToken, new
    {
        fullName = "Maria Proprietária",
        phoneNumber = "+55 (21) 97777-6666",
        emailAddress = "MARIA@EXAMPLE.COM"
    });
    Ensure(ownerCustomerCreate.StatusCode == HttpStatusCode.Created,
        "Owner can create a tenant-scoped customer with a normalized contact.");
    var ownerCustomer = await ownerCustomerCreate.Content.ReadFromJsonAsync<JsonElement>();
    var ownerCustomerId = ownerCustomer.GetProperty("id").GetGuid();
    Ensure(ownerCustomer.GetProperty("emailAddress").GetString() == "maria@example.com",
        "Customer API normalizes email addresses before persistence.");
    using (var updateCustomerRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/customers/{ownerCustomerId}"))
    {
        updateCustomerRequest.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        updateCustomerRequest.Content = JsonContent.Create(new
        {
            fullName = "Maria Atualizada",
            phoneNumber = (string?)null,
            emailAddress = "maria.updated@example.com"
        });
        using var updateCustomerResponse = await mfaClient.SendAsync(updateCustomerRequest);
        var updatedCustomer = await updateCustomerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(updateCustomerResponse.StatusCode == HttpStatusCode.OK &&
               updatedCustomer.GetProperty("fullName").GetString() == "Maria Atualizada" &&
               updatedCustomer.GetProperty("emailAddress").GetString() == "maria.updated@example.com",
            "Owner can update customer contact data through the tenant-scoped API.");
    }
    var ownerArchiveCustomer = await PostWithCsrfAsync(
        mfaClient, $"/api/v1/customers/{ownerCustomerId}/archive", ownerAuthenticatedToken, new { });
    var archivedCustomer = await ownerArchiveCustomer.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerArchiveCustomer.StatusCode == HttpStatusCode.OK &&
           archivedCustomer.GetProperty("isArchived").GetBoolean(),
        "Owner can archive a customer without physically deleting the record.");

    using (var ownerBusinessHoursPut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/business-hours"))
    {
        ownerBusinessHoursPut.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        ownerBusinessHoursPut.Content = JsonContent.Create(new
        {
            intervals = new[]
            {
                new { isoDayOfWeek = 1, startTime = "09:00", endTime = "12:00" },
                new { isoDayOfWeek = 1, startTime = "13:00", endTime = "18:00" }
            }
        });
        using var ownerBusinessHoursResponse = await mfaClient.SendAsync(ownerBusinessHoursPut);
        var ownerBusinessHoursBody = await ownerBusinessHoursResponse.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(ownerBusinessHoursResponse.StatusCode == HttpStatusCode.OK &&
               ownerBusinessHoursBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
               ownerBusinessHoursBody.GetProperty("intervals").GetArrayLength() == 2 &&
               ownerBusinessHoursBody.GetProperty("intervals")[0].GetProperty("startTime").GetString() == "09:00" &&
               ownerBusinessHoursBody.GetProperty("intervals")[1].GetProperty("startTime").GetString() == "13:00",
            "Owner can configure split weekly opening intervals in the tenant's local time zone.");
    }
    var ownerBusinessHoursGet = await mfaClient.GetAsync("/api/v1/business-hours");
    var ownerBusinessHoursGetBody = await ownerBusinessHoursGet.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerBusinessHoursGet.StatusCode == HttpStatusCode.OK &&
           ownerBusinessHoursGetBody.GetProperty("intervals").GetArrayLength() == 2,
        "Business-hours configuration persists and is returned to authorized tenant users.");

    using (var overlappingBusinessHoursPut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/business-hours"))
    {
        overlappingBusinessHoursPut.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        overlappingBusinessHoursPut.Content = JsonContent.Create(new
        {
            intervals = new[]
            {
                new { isoDayOfWeek = 1, startTime = "09:00", endTime = "12:00" },
                new { isoDayOfWeek = 1, startTime = "11:00", endTime = "14:00" }
            }
        });
        using var overlappingBusinessHoursResponse = await mfaClient.SendAsync(overlappingBusinessHoursPut);
        Ensure(overlappingBusinessHoursResponse.StatusCode == HttpStatusCode.BadRequest,
            "Business-hours API rejects overlapping intervals before persistence.");
    }
    using (var invalidBusinessHoursPut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/business-hours"))
    {
        invalidBusinessHoursPut.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        invalidBusinessHoursPut.Content = JsonContent.Create(new
        {
            intervals = new[] { new { isoDayOfWeek = 8, startTime = "25:00", endTime = "26:00" } }
        });
        using var invalidBusinessHoursResponse = await mfaClient.SendAsync(invalidBusinessHoursPut);
        Ensure(invalidBusinessHoursResponse.StatusCode == HttpStatusCode.BadRequest,
            "Business-hours API rejects invalid ISO weekdays and non-local-time input.");
    }
    var businessHoursAfterInvalidUpdates = await mfaClient.GetAsync("/api/v1/business-hours");
    var businessHoursAfterInvalidUpdatesBody = await businessHoursAfterInvalidUpdates.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(businessHoursAfterInvalidUpdatesBody.GetProperty("intervals").GetArrayLength() == 2,
        "Rejected business-hours updates leave the last valid schedule unchanged.");

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

    using (var ownerProfessionalAvailabilityPut = new HttpRequestMessage(
        HttpMethod.Put, $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability"))
    {
        ownerProfessionalAvailabilityPut.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        ownerProfessionalAvailabilityPut.Content = JsonContent.Create(new
        {
            intervals = new[]
            {
                new { isoDayOfWeek = 1, startTime = "09:00", endTime = "12:00" },
                new { isoDayOfWeek = 1, startTime = "13:00", endTime = "18:00" }
            }
        });
        using var ownerProfessionalAvailabilityResponse = await mfaClient.SendAsync(ownerProfessionalAvailabilityPut);
        var ownerProfessionalAvailabilityBody =
            await ownerProfessionalAvailabilityResponse.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(ownerProfessionalAvailabilityResponse.StatusCode == HttpStatusCode.OK &&
               ownerProfessionalAvailabilityBody.GetProperty("professionalId").GetGuid() == assignedAssignedProfessionalProfileId &&
               ownerProfessionalAvailabilityBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
               ownerProfessionalAvailabilityBody.GetProperty("intervals").GetArrayLength() == 2 &&
               ownerProfessionalAvailabilityBody.GetProperty("intervals")[0].GetProperty("startTime").GetString() == "09:00" &&
               ownerProfessionalAvailabilityBody.GetProperty("intervals")[1].GetProperty("startTime").GetString() == "13:00",
            "Owner can configure a professional's recurring local-time availability.");
    }
    var ownerProfessionalAvailabilityGet = await mfaClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability");
    var ownerProfessionalAvailabilityGetBody =
        await ownerProfessionalAvailabilityGet.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(ownerProfessionalAvailabilityGet.StatusCode == HttpStatusCode.OK &&
           ownerProfessionalAvailabilityGetBody.GetProperty("intervals").GetArrayLength() == 2,
        "Owner can read the professional's persisted weekly availability.");

    using (var invalidProfessionalAvailabilityPut = new HttpRequestMessage(
        HttpMethod.Put, $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability"))
    {
        invalidProfessionalAvailabilityPut.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        invalidProfessionalAvailabilityPut.Content = JsonContent.Create(new
        {
            intervals = new[]
            {
                new { isoDayOfWeek = 1, startTime = "09:00", endTime = "12:00" },
                new { isoDayOfWeek = 1, startTime = "11:00", endTime = "14:00" }
            }
        });
        using var invalidProfessionalAvailabilityResponse = await mfaClient.SendAsync(invalidProfessionalAvailabilityPut);
        Ensure(invalidProfessionalAvailabilityResponse.StatusCode == HttpStatusCode.BadRequest,
            "Professional availability API rejects overlapping intervals.");
    }
    var professionalAvailabilityAfterInvalidUpdate = await mfaClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability");
    var professionalAvailabilityAfterInvalidUpdateBody =
        await professionalAvailabilityAfterInvalidUpdate.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(professionalAvailabilityAfterInvalidUpdateBody.GetProperty("intervals").GetArrayLength() == 2,
        "Rejected professional availability updates leave the last valid schedule unchanged.");

    var createPartialTimeOff = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        ownerAuthenticatedToken,
        new { date = "2026-12-01", startTime = "10:00", endTime = "11:00" });
    Ensure(createPartialTimeOff.StatusCode == HttpStatusCode.Created,
        "Owner can create date-specific partial-day time off for a professional.");
    var createdPartialTimeOffBody = await createPartialTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    var createdPartialTimeOffId = createdPartialTimeOffBody.GetProperty("id").GetGuid();
    Ensure(createdPartialTimeOffBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
           createdPartialTimeOffBody.GetProperty("date").GetString() == "2026-12-01" &&
           createdPartialTimeOffBody.GetProperty("startTime").GetString() == "10:00" &&
           createdPartialTimeOffBody.GetProperty("endTime").GetString() == "11:00",
        "Time-off API preserves tenant-local calendar date, time zone and wall-clock values.");

    var overlappingTimeOff = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        ownerAuthenticatedToken,
        new { date = "2026-12-01", startTime = "10:30", endTime = "11:30" });
    Ensure(overlappingTimeOff.StatusCode == HttpStatusCode.BadRequest,
        "Overlapping date-specific time-off intervals are rejected without replacing the existing entry.");

    var createFullDayTimeOff = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        ownerAuthenticatedToken,
        new { date = "2026-12-02", startTime = (string?)null, endTime = (string?)null });
    Ensure(createFullDayTimeOff.StatusCode == HttpStatusCode.Created,
        "Owner can create full-day time off using a null local-time pair.");
    var createdFullDayTimeOffBody = await createFullDayTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    var createdFullDayTimeOffId = createdFullDayTimeOffBody.GetProperty("id").GetGuid();
    Ensure(createdFullDayTimeOffBody.GetProperty("startTime").ValueKind == JsonValueKind.Null &&
           createdFullDayTimeOffBody.GetProperty("endTime").ValueKind == JsonValueKind.Null,
        "Full-day time off is represented explicitly without inventing local hours.");

    var partialDuringFullDay = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        ownerAuthenticatedToken,
        new { date = "2026-12-02", startTime = "10:00", endTime = "11:00" });
    Ensure(partialDuringFullDay.StatusCode == HttpStatusCode.BadRequest,
        "Full-day time off cannot coexist with partial-day entries on the same date.");

    var listedTimeOff = await mfaClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off?from=2026-12-01&to=2026-12-02");
    var listedTimeOffBody = await listedTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(listedTimeOff.StatusCode == HttpStatusCode.OK &&
           listedTimeOffBody.GetArrayLength() == 2,
        "Owner can list a bounded inclusive range of persisted local-date time-off entries.");

    var invalidTimeOffRange = await mfaClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off?from=2026-12-03&to=2026-12-01");
    Ensure(invalidTimeOffRange.StatusCode == HttpStatusCode.BadRequest,
        "Time-off listing rejects an inverted date range.");

    var archivedTimeOff = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off/{createdPartialTimeOffId}/archive",
        ownerAuthenticatedToken,
        new { });
    Ensure(archivedTimeOff.StatusCode == HttpStatusCode.OK,
        "Owner can archive a time-off entry without physically deleting its audit history.");

    var listedTimeOffAfterArchive = await mfaClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off?from=2026-12-01&to=2026-12-02");
    var listedTimeOffAfterArchiveBody = await listedTimeOffAfterArchive.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(listedTimeOffAfterArchiveBody.GetArrayLength() == 1 &&
           listedTimeOffAfterArchiveBody[0].GetProperty("id").GetGuid() == createdFullDayTimeOffId,
        "Archived time-off entries are excluded from active listings while full-day entries remain.");

    var professionalTimeOffAudit = await ReadProfessionalAuditAsync(
        migratorConnectionString, ownerTenantId, assignedAssignedProfessionalProfileId);
    Ensure(professionalTimeOffAudit.Contains("professional_time_off.created") &&
           professionalTimeOffAudit.Contains("professional_time_off.archived"),
        "Time-off creation and archive are recorded in the mandatory tenant audit ledger.");

    var availableBeforeTimeOff = await mfaClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={assignedAssignedProfessionalProfileId:D}");
    var availableBeforeTimeOffBody = await availableBeforeTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(availableBeforeTimeOff.StatusCode == HttpStatusCode.OK &&
           availableBeforeTimeOffBody.GetProperty("timeZoneId").GetString() == "America/Sao_Paulo" &&
           availableBeforeTimeOffBody.GetProperty("professionals").GetArrayLength() == 1 &&
           availableBeforeTimeOffBody.GetProperty("professionals")[0].GetProperty("windows").GetArrayLength() == 2,
        "Authoritative availability intersects tenant business hours, professional recurring hours and active service assignment.");

    var createAvailabilityTimeOff = await PostWithCsrfAsync(
        mfaClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        ownerAuthenticatedToken,
        new { date = "2026-12-07", startTime = "10:00", endTime = "10:30" });
    Ensure(createAvailabilityTimeOff.StatusCode == HttpStatusCode.Created,
        "Owner can create a partial-day exception for authoritative availability calculation.");

    var availableAfterTimeOff = await mfaClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={assignedAssignedProfessionalProfileId:D}");
    var availableAfterTimeOffBody = await availableAfterTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    var availabilityWindows = availableAfterTimeOffBody.GetProperty("professionals")[0].GetProperty("windows");
    var firstAvailabilityStart = DateTimeOffset.Parse(availabilityWindows[0].GetProperty("earliestStartUtc").GetString()!);
    var firstAvailabilityLatest = DateTimeOffset.Parse(availabilityWindows[0].GetProperty("latestStartUtc").GetString()!);
    var secondAvailabilityStart = DateTimeOffset.Parse(availabilityWindows[1].GetProperty("earliestStartUtc").GetString()!);
    var secondAvailabilityLatest = DateTimeOffset.Parse(availabilityWindows[1].GetProperty("latestStartUtc").GetString()!);
    var thirdAvailabilityStart = DateTimeOffset.Parse(availabilityWindows[2].GetProperty("earliestStartUtc").GetString()!);
    var thirdAvailabilityLatest = DateTimeOffset.Parse(availabilityWindows[2].GetProperty("latestStartUtc").GetString()!);
    var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    Ensure(availableAfterTimeOff.StatusCode == HttpStatusCode.OK &&
           availabilityWindows.GetArrayLength() == 3 &&
           TimeZoneInfo.ConvertTime(firstAvailabilityStart, saoPaulo).TimeOfDay == new TimeSpan(9, 5, 0) &&
           TimeZoneInfo.ConvertTime(firstAvailabilityLatest, saoPaulo).TimeOfDay == new TimeSpan(9, 5, 0) &&
           TimeZoneInfo.ConvertTime(secondAvailabilityStart, saoPaulo).TimeOfDay == new TimeSpan(10, 35, 0) &&
           TimeZoneInfo.ConvertTime(secondAvailabilityLatest, saoPaulo).TimeOfDay == new TimeSpan(11, 5, 0) &&
           TimeZoneInfo.ConvertTime(thirdAvailabilityStart, saoPaulo).TimeOfDay == new TimeSpan(13, 5, 0) &&
           TimeZoneInfo.ConvertTime(thirdAvailabilityLatest, saoPaulo).TimeOfDay == new TimeSpan(17, 5, 0),
        "Authoritative availability subtracts partial time off, preserves an exact-fit buffered slot and applies service duration and before/after buffers.");
    var bookingCustomerResponse = await PostWithCsrfAsync(mfaClient, "/api/v1/customers", ownerAuthenticatedToken, new
    {
        fullName = "Cliente de Agendamento",
        phoneNumber = "+55 (11) 98888-7777",
        emailAddress = "booking.customer@example.invalid"
    });
    Ensure(bookingCustomerResponse.StatusCode == HttpStatusCode.Created,
        "Booking fixture customer is created through the tenant-scoped customer API.");
    var bookingCustomer = await bookingCustomerResponse.Content.ReadFromJsonAsync<JsonElement>();
    var bookingCustomerId = bookingCustomer.GetProperty("id").GetGuid();
    var bookingStartUtc = new DateTimeOffset(2026, 12, 7, 12, 5, 0, TimeSpan.Zero);
    var bookingIdempotencyKey = Guid.NewGuid().ToString("N");

    async Task<HttpResponseMessage> SendBookingAsync(DateTimeOffset startAtUtc)
    {
        var bookingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/appointments");
        bookingRequest.Headers.Add("X-CSRF-TOKEN", ownerAuthenticatedToken);
        bookingRequest.Headers.Add("Idempotency-Key", bookingIdempotencyKey);
        bookingRequest.Content = JsonContent.Create(new
        {
            customerId = bookingCustomerId,
            professionalId = assignedAssignedProfessionalProfileId,
            serviceId = salonServiceId,
            startAtUtc
        });
        return await mfaClient.SendAsync(bookingRequest);
    }

    using (var createdBookingResponse = await SendBookingAsync(bookingStartUtc))
    {
        Ensure(createdBookingResponse.StatusCode == HttpStatusCode.Created,
            "Owner can create a tenant appointment inside authoritative service availability.");
        var createdBooking = await createdBookingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var appointmentId = createdBooking.GetProperty("id").GetGuid();
        Ensure(createdBooking.GetProperty("customerNameSnapshot").GetString() == "Cliente de Agendamento" &&
               createdBooking.GetProperty("professionalNameSnapshot").GetString() == "HTTP Synthetic Audit Target" &&
               createdBooking.GetProperty("serviceNameSnapshot").GetString() == "Corte de cabelo" &&
               createdBooking.GetProperty("priceMinorUnitsSnapshot").GetInt64() == 7500 &&
               createdBooking.GetProperty("durationMinutesSnapshot").GetInt32() == 45 &&
               createdBooking.GetProperty("bufferBeforeMinutesSnapshot").GetInt32() == 5 &&
               createdBooking.GetProperty("bufferAfterMinutesSnapshot").GetInt32() == 10 &&
               createdBooking.GetProperty("status").GetString() == "Confirmed",
            "Appointment persists immutable customer, professional, service, price, duration and buffer snapshots.");

        using (var replayResponse = await SendBookingAsync(bookingStartUtc))
        {
            var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(replayResponse.StatusCode == HttpStatusCode.OK &&
                   replay.GetProperty("id").GetGuid() == appointmentId,
                "Same idempotency key and request replay the original appointment.");
        }
        using (var conflictingReplay = await SendBookingAsync(bookingStartUtc.AddMinutes(1)))
            Ensure(conflictingReplay.StatusCode == HttpStatusCode.Conflict,
                "Reusing an idempotency key with a different request hash is rejected.");

        using (var getBooking = await mfaClient.GetAsync($"/api/v1/appointments/{appointmentId:D}"))
        {
            var fetched = await getBooking.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(getBooking.StatusCode == HttpStatusCode.OK &&
                   fetched.GetProperty("id").GetGuid() == appointmentId,
                "Owner can retrieve the persisted appointment by tenant-scoped ID.");
        }
        using (var listBookings = await mfaClient.GetAsync(
            "/api/v1/appointments?fromUtc=2026-12-07T00:00:00Z&toUtc=2026-12-08T00:00:00Z"))
        {
            var listed = await listBookings.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(listBookings.StatusCode == HttpStatusCode.OK &&
                   listed.GetProperty("totalCount").GetInt32() == 1 &&
                   listed.GetProperty("items")[0].GetProperty("id").GetGuid() == appointmentId,
                "Owner can list tenant appointments within a bounded UTC range.");
        }

        var availabilityAfterBooking = await mfaClient.GetAsync(
            $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={assignedAssignedProfessionalProfileId:D}");
        var availabilityAfterBookingBody = await availabilityAfterBooking.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(availabilityAfterBooking.StatusCode == HttpStatusCode.OK &&
               availabilityAfterBookingBody.GetProperty("professionals")[0].GetProperty("windows").GetArrayLength() == 2,
            "Authoritative availability subtracts the committed appointment and its occupancy buffers.");

        await using var auditConnection = new NpgsqlConnection(migratorConnectionString);
        await auditConnection.OpenAsync();
        await using var auditTransaction = await auditConnection.BeginTransactionAsync();
        await using (var auditRole = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", auditConnection, auditTransaction))
            await auditRole.ExecuteNonQueryAsync();
        await using var auditCommand = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM atendee.appointment_events
                 WHERE tenant_id = @tenant AND appointment_id = @appointment AND event_type = 'appointment.created') +
                (SELECT count(*) FROM atendee.tenant_audit_events
                 WHERE tenant_id = @tenant AND resource_id = @appointment AND event_type = 'appointment.created') +
                (SELECT count(*) FROM atendee.transactional_outbox
                 WHERE tenant_id = @tenant AND aggregate_id = @appointment AND event_type = 'appointment.created')
            """, auditConnection, auditTransaction);
        auditCommand.Parameters.AddWithValue("tenant", ownerTenantId);
        auditCommand.Parameters.AddWithValue("appointment", appointmentId);
        Ensure(Convert.ToInt32(await auditCommand.ExecuteScalarAsync()) == 3,
            "Appointment, immutable event, tenant audit and transactional outbox commit atomically.");
        await auditTransaction.CommitAsync();

        var overlapRejected = false;
        await using (var overlapConnection = new NpgsqlConnection(migratorConnectionString))
        {
            await overlapConnection.OpenAsync();
            await using var tx = await overlapConnection.BeginTransactionAsync();
            await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", overlapConnection, tx))
                await role.ExecuteNonQueryAsync();
            await using (var actor = new NpgsqlCommand(
                "SELECT set_config('app.actor_id', @actor, true)", overlapConnection, tx))
            {
                actor.Parameters.AddWithValue("actor", ownerUserId.ToString("D"));
                await actor.ExecuteScalarAsync();
            }
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO atendee.appointments
                (
                    tenant_id, appointment_id, customer_id, professional_id, service_id,
                    customer_name_snapshot, customer_phone_snapshot, professional_name_snapshot,
                    service_name_snapshot, duration_minutes_snapshot, price_minor_units_snapshot,
                    currency_snapshot, buffer_before_minutes_snapshot, buffer_after_minutes_snapshot,
                    start_at_utc, end_at_utc, occupancy_start_at_utc, occupancy_end_at_utc,
                    status, idempotency_key, request_hash, created_by_identity_user_id,
                    created_at_utc, updated_at_utc, cancelled_at_utc
                )
                SELECT tenant_id, gen_random_uuid(), customer_id, professional_id, service_id,
                    customer_name_snapshot, customer_phone_snapshot, professional_name_snapshot,
                    service_name_snapshot, duration_minutes_snapshot, price_minor_units_snapshot,
                    currency_snapshot, buffer_before_minutes_snapshot, buffer_after_minutes_snapshot,
                    start_at_utc, end_at_utc, occupancy_start_at_utc, occupancy_end_at_utc,
                    status, @key, repeat('b', 64), created_by_identity_user_id, now(), now(), NULL
                FROM atendee.appointments
                WHERE tenant_id = @tenant AND appointment_id = @appointment
                """, overlapConnection, tx);
            insert.Parameters.AddWithValue("key", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("tenant", ownerTenantId);
            insert.Parameters.AddWithValue("appointment", appointmentId);
            await insert.ExecuteNonQueryAsync();
            try { await tx.CommitAsync(); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
            { overlapRejected = true; }
        }
        Ensure(overlapRejected,
            "Deferred PostgreSQL constraint trigger rejects overlapping occupancy even when a writer bypasses the availability API.");
        using (var illegalTransition = await PostWithCsrfAsync(
            mfaClient, $"/api/v1/appointments/{appointmentId:D}/transitions",
            ownerAuthenticatedToken, new { status = "Completed" }))
            Ensure(illegalTransition.StatusCode == HttpStatusCode.Conflict,
                "Appointment lifecycle rejects skipping directly from Confirmed to Completed.");

        using (var checkedIn = await PostWithCsrfAsync(
            mfaClient, $"/api/v1/appointments/{appointmentId:D}/transitions",
            ownerAuthenticatedToken, new { status = "CheckedIn" }))
        {
            var result = await checkedIn.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(checkedIn.StatusCode == HttpStatusCode.OK &&
                   result.GetProperty("status").GetString() == "CheckedIn",
                "Owner can check in a confirmed appointment through the legal lifecycle.");
        }
        using (var inService = await PostWithCsrfAsync(
            mfaClient, $"/api/v1/appointments/{appointmentId:D}/transitions",
            ownerAuthenticatedToken, new { status = "InService" }))
        {
            var result = await inService.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(inService.StatusCode == HttpStatusCode.OK &&
                   result.GetProperty("status").GetString() == "InService",
                "Appointment can transition from CheckedIn to InService.");
        }
        using (var completed = await PostWithCsrfAsync(
            mfaClient, $"/api/v1/appointments/{appointmentId:D}/transitions",
            ownerAuthenticatedToken, new { status = "Completed" }))
        {
            var result = await completed.Content.ReadFromJsonAsync<JsonElement>();
            Ensure(completed.StatusCode == HttpStatusCode.OK &&
                   result.GetProperty("status").GetString() == "Completed",
                "Appointment can complete only after entering service.");
        }

        var availabilityAfterCompletion = await mfaClient.GetAsync(
            $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={assignedAssignedProfessionalProfileId:D}");
        var availabilityAfterCompletionBody = await availabilityAfterCompletion.Content.ReadFromJsonAsync<JsonElement>();
        Ensure(availabilityAfterCompletion.StatusCode == HttpStatusCode.OK &&
               availabilityAfterCompletionBody.GetProperty("professionals")[0].GetProperty("windows").GetArrayLength() == 3,
            "Completed appointments remain in history but no longer block future availability.");

        await using var lifecycleAuditConnection = new NpgsqlConnection(migratorConnectionString);
        await lifecycleAuditConnection.OpenAsync();
        await using var lifecycleAuditTransaction = await lifecycleAuditConnection.BeginTransactionAsync();
        await using (var auditRole = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", lifecycleAuditConnection, lifecycleAuditTransaction))
            await auditRole.ExecuteNonQueryAsync();
        await using var lifecycleAuditCommand = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM atendee.appointment_events
                 WHERE tenant_id = @tenant AND appointment_id = @appointment) +
                (SELECT count(*) FROM atendee.tenant_audit_events
                 WHERE tenant_id = @tenant AND resource_id = @appointment) +
                (SELECT count(*) FROM atendee.transactional_outbox
                 WHERE tenant_id = @tenant AND aggregate_id = @appointment)
            """, lifecycleAuditConnection, lifecycleAuditTransaction);
        lifecycleAuditCommand.Parameters.AddWithValue("tenant", ownerTenantId);
        lifecycleAuditCommand.Parameters.AddWithValue("appointment", appointmentId);
        Ensure(Convert.ToInt32(await lifecycleAuditCommand.ExecuteScalarAsync()) == 12,
            "Every legal lifecycle transition appends one immutable event, audit record and outbox message atomically.");
        await lifecycleAuditTransaction.CommitAsync();
    }

    var unassignedProfessionalAvailability = await mfaClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={Guid.NewGuid():D}&professionalId={assignedAssignedProfessionalProfileId:D}");
    Ensure(unassignedProfessionalAvailability.StatusCode == HttpStatusCode.NotFound,
        "Availability does not expose a service that is not active in the tenant catalog.");

    var invalidAvailabilityRange = await mfaClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2027-01-08&serviceId={salonServiceId:D}");
    Ensure(invalidAvailabilityRange.StatusCode == HttpStatusCode.BadRequest,
        "Authoritative availability rejects date ranges exceeding the bounded query window.");
    await using (var crossTenantTimeOffConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await crossTenantTimeOffConnection.OpenAsync();
        await using var transaction = await crossTenantTimeOffConnection.BeginTransactionAsync();
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_runtime", crossTenantTimeOffConnection, transaction))
            await role.ExecuteNonQueryAsync();
        await using (var tenant = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, true)", crossTenantTimeOffConnection, transaction))
        {
            tenant.Parameters.AddWithValue("tenant", tenantId.ToString("D"));
            await tenant.ExecuteScalarAsync();
        }
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM atendee.professional_time_off WHERE professional_id = @professional",
            crossTenantTimeOffConnection, transaction);
        count.Parameters.AddWithValue("professional", assignedAssignedProfessionalProfileId);
        Ensure(Convert.ToInt64(await count.ExecuteScalarAsync()) == 0,
            "PostgreSQL RLS hides professional time off from another salon even when the profile UUID is known.");
        await transaction.RollbackAsync();
    }

    await using (var unscopedTimeOffConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await unscopedTimeOffConnection.OpenAsync();
        await using var transaction = await unscopedTimeOffConnection.BeginTransactionAsync();
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_runtime", unscopedTimeOffConnection, transaction))
            await role.ExecuteNonQueryAsync();
        await using (var clearTenant = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', '', true)", unscopedTimeOffConnection, transaction))
            await clearTenant.ExecuteScalarAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM atendee.professional_time_off", unscopedTimeOffConnection, transaction);
        Ensure(Convert.ToInt64(await count.ExecuteScalarAsync()) == 0,
            "Runtime SQL without tenant context cannot read professional time-off records.");
        await using var privileges = new NpgsqlCommand(
            "SELECT has_table_privilege('atendee_runtime', 'atendee.professional_time_off', 'DELETE') OR " +
            "has_table_privilege('atendee_runtime', 'atendee.professional_time_off', 'TRUNCATE')",
            unscopedTimeOffConnection, transaction);
        Ensure(!Convert.ToBoolean(await privileges.ExecuteScalarAsync()),
            "Runtime database role cannot physically delete or truncate professional time-off records.");
        await transaction.RollbackAsync();
    }

    var databaseTimeOffOverlapRejected = false;
    await using (var overlapTimeOffConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await overlapTimeOffConnection.OpenAsync();
        await using var transaction = await overlapTimeOffConnection.BeginTransactionAsync();
        try
        {
            await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_runtime", overlapTimeOffConnection, transaction))
                await role.ExecuteNonQueryAsync();
            await using (var context = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenant, true), set_config('app.actor_id', @actor, true)",
                overlapTimeOffConnection, transaction))
            {
                context.Parameters.AddWithValue("tenant", ownerTenantId.ToString("D"));
                context.Parameters.AddWithValue("actor", ownerUserId.ToString("D"));
                await context.ExecuteNonQueryAsync();
            }
            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO atendee.professional_time_off
                    (tenant_id, time_off_id, professional_id, local_date, start_time, end_time,
                     is_active, created_at_utc, updated_at_utc)
                VALUES
                    (@tenant, @id, @professional, DATE '2026-12-02', TIME '10:00', TIME '11:00',
                     true, now(), now())
                """, overlapTimeOffConnection, transaction))
            {
                insert.Parameters.AddWithValue("tenant", ownerTenantId);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("professional", assignedAssignedProfessionalProfileId);
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch (PostgresException exception) when (exception.SqlState == "23P01")
        {
            databaseTimeOffOverlapRejected = true;
        }
    }
    Ensure(databaseTimeOffOverlapRejected,
        "PostgreSQL deferred constraint trigger rejects partial time off that conflicts with a full-day entry even when domain validation is bypassed.");
    await using (var crossTenantAvailabilityConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await crossTenantAvailabilityConnection.OpenAsync();
        await using var crossTenantAvailabilityTransaction =
            await crossTenantAvailabilityConnection.BeginTransactionAsync();
        await using (var setRuntimeRole = new NpgsqlCommand(
            "SET LOCAL ROLE atendee_runtime",
            crossTenantAvailabilityConnection,
            crossTenantAvailabilityTransaction))
            await setRuntimeRole.ExecuteNonQueryAsync();
        await using (var setOtherSalonContext = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            crossTenantAvailabilityConnection,
            crossTenantAvailabilityTransaction))
        {
            setOtherSalonContext.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
            await setOtherSalonContext.ExecuteScalarAsync();
        }
        await using var crossTenantAvailabilityCount = new NpgsqlCommand(
            "SELECT count(*) FROM atendee.professional_availability WHERE professional_id = @professional_id",
            crossTenantAvailabilityConnection,
            crossTenantAvailabilityTransaction);
        crossTenantAvailabilityCount.Parameters.AddWithValue(
            "professional_id", assignedAssignedProfessionalProfileId);
        var hiddenAvailabilityCount = Convert.ToInt64(await crossTenantAvailabilityCount.ExecuteScalarAsync());
        Ensure(hiddenAvailabilityCount == 0,
            "PostgreSQL RLS hides professional availability from another salon even when the profile UUID is known.");
        await crossTenantAvailabilityTransaction.RollbackAsync();
    }

    await using (var directAvailabilityConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await directAvailabilityConnection.OpenAsync();
        await using var directAvailabilityTransaction = await directAvailabilityConnection.BeginTransactionAsync();
        await using (var setRuntimeRole = new NpgsqlCommand("SET LOCAL ROLE atendee_runtime", directAvailabilityConnection, directAvailabilityTransaction))
            await setRuntimeRole.ExecuteNonQueryAsync();
        await using (var clearTenantContext = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', '', true)", directAvailabilityConnection, directAvailabilityTransaction))
            await clearTenantContext.ExecuteScalarAsync();
        await using var directAvailabilityCount = new NpgsqlCommand(
            "SELECT count(*) FROM atendee.professional_availability",
            directAvailabilityConnection,
            directAvailabilityTransaction);
        var directCount = Convert.ToInt64(await directAvailabilityCount.ExecuteScalarAsync());
        Ensure(directCount == 0,
            "Runtime SQL without the tenant transaction context cannot read professional availability rows.");
        await directAvailabilityTransaction.RollbackAsync();
    }

    var databaseAvailabilityOverlapRejected = false;
    await using (var overlapAvailabilityConnection = new NpgsqlConnection(tenantConnectionString))
    {
        await overlapAvailabilityConnection.OpenAsync();
        await using var overlapAvailabilityTransaction = await overlapAvailabilityConnection.BeginTransactionAsync();
        try
        {
            await using (var setRuntimeRole = new NpgsqlCommand(
                "SET LOCAL ROLE atendee_runtime", overlapAvailabilityConnection, overlapAvailabilityTransaction))
                await setRuntimeRole.ExecuteNonQueryAsync();
            await using (var setAvailabilityContext = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenant_id, true), set_config('app.actor_id', @actor_id, true)",
                overlapAvailabilityConnection,
                overlapAvailabilityTransaction))
            {
                setAvailabilityContext.Parameters.AddWithValue("tenant_id", ownerTenantId.ToString("D"));
                setAvailabilityContext.Parameters.AddWithValue("actor_id", ownerUserId.ToString("D"));
                await setAvailabilityContext.ExecuteNonQueryAsync();
            }
            await using (var insertOverlappingAvailability = new NpgsqlCommand(
                """
                INSERT INTO atendee.professional_availability
                    (tenant_id, availability_id, professional_id, iso_day_of_week, start_time, end_time,
                     is_active, created_at_utc, updated_at_utc)
                VALUES
                    (@tenant_id, @availability_id, @professional_id, 1, TIME '10:00', TIME '11:00',
                     true, now(), now())
                """,
                overlapAvailabilityConnection,
                overlapAvailabilityTransaction))
            {
                insertOverlappingAvailability.Parameters.AddWithValue("tenant_id", ownerTenantId);
                insertOverlappingAvailability.Parameters.AddWithValue("availability_id", Guid.NewGuid());
                insertOverlappingAvailability.Parameters.AddWithValue("professional_id", assignedAssignedProfessionalProfileId);
                await insertOverlappingAvailability.ExecuteNonQueryAsync();
            }
            await overlapAvailabilityTransaction.CommitAsync();
        }
        catch (PostgresException exception) when (exception.SqlState == "23P01")
        {
            // The initially-deferred constraint trigger is evaluated by COMMIT.
            // Npgsql marks the transaction complete after the server rejects it.
            databaseAvailabilityOverlapRejected = true;
        }
    }
    Ensure(databaseAvailabilityOverlapRejected,
        "PostgreSQL deferred constraint trigger rejects overlapping professional availability even when domain validation is bypassed.");

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

    var assignedOwnAvailability = await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals/me/availability");
    var assignedOwnAvailabilityBody = await assignedOwnAvailability.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedOwnAvailability.StatusCode == HttpStatusCode.OK &&
           assignedOwnAvailabilityBody.GetProperty("professionalId").GetGuid() == assignedAssignedProfessionalProfileId &&
           assignedOwnAvailabilityBody.GetProperty("intervals").GetArrayLength() == 2,
        "Professional can read only their own recurring availability through the /me route.");
    var assignedOwnTimeOff = await assignedAssignedProfessionalClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off?from=2026-12-01&to=2026-12-02");
    var assignedOwnTimeOffBody = await assignedOwnTimeOff.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(assignedOwnTimeOff.StatusCode == HttpStatusCode.OK &&
           assignedOwnTimeOffBody.GetArrayLength() == 1 &&
           assignedOwnTimeOffBody[0].GetProperty("date").GetString() == "2026-12-02",
        "Professional can read only their own active time-off entries.");
    var assignedOwnAvailabilityById = await assignedAssignedProfessionalClient.GetAsync(
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability");
    Ensure(assignedOwnAvailabilityById.StatusCode == HttpStatusCode.OK,
        "Professional can read their own availability when addressing their own profile ID.");
    var professionalServiceAvailability = await assignedAssignedProfessionalClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={assignedAssignedProfessionalProfileId:D}");
    var professionalServiceAvailabilityBody =
        await professionalServiceAvailability.Content.ReadFromJsonAsync<JsonElement>();
    Ensure(professionalServiceAvailability.StatusCode == HttpStatusCode.OK &&
           professionalServiceAvailabilityBody.GetProperty("professionals").GetArrayLength() == 1 &&
           professionalServiceAvailabilityBody.GetProperty("professionals")[0].GetProperty("professionalId").GetGuid() ==
               assignedAssignedProfessionalProfileId,
        "Professional can query service availability for their own profile only.");
    var professionalCannotQueryOtherAvailability = await assignedAssignedProfessionalClient.GetAsync(
        $"/api/v1/availability?from=2026-12-07&to=2026-12-07&serviceId={salonServiceId:D}&professionalId={Guid.NewGuid():D}");
    Ensure(professionalCannotQueryOtherAvailability.StatusCode == HttpStatusCode.Forbidden,
        "Professional cannot query another profile's service availability by supplying an arbitrary UUID.");
    var unknownProfessionalAvailability = await assignedAssignedProfessionalClient.GetAsync(
        $"/api/v1/professionals/{Guid.NewGuid()}/availability");
    Ensure(unknownProfessionalAvailability.StatusCode == HttpStatusCode.NotFound,
        "Professional cannot enumerate another professional's availability by arbitrary UUID.");
    var professionalAvailabilityCsrf = await assignedAssignedProfessionalClient.GetAsync("/api/v1/auth/antiforgery");
    var professionalAvailabilityCsrfBody = await professionalAvailabilityCsrf.Content.ReadFromJsonAsync<JsonElement>();
    var professionalAvailabilityRequestToken = professionalAvailabilityCsrfBody.GetProperty("requestToken").GetString();
    Ensure(professionalAvailabilityCsrf.IsSuccessStatusCode &&
           !string.IsNullOrWhiteSpace(professionalAvailabilityRequestToken),
        "Professional obtains a fresh antiforgery token after login before attempting availability changes.");
    var professionalCannotCreateTimeOff = await PostWithCsrfAsync(
        assignedAssignedProfessionalClient,
        $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/time-off",
        professionalAvailabilityRequestToken!,
        new { date = "2026-12-03", startTime = (string?)null, endTime = (string?)null });
    Ensure(professionalCannotCreateTimeOff.StatusCode == HttpStatusCode.Forbidden,
        "Professional can read but cannot create team time-off entries; management remains Owner/Manager-only.");
    using (var professionalAvailabilityPut = new HttpRequestMessage(
        HttpMethod.Put, $"/api/v1/professionals/{assignedAssignedProfessionalProfileId}/availability"))
    {
        professionalAvailabilityPut.Headers.Add("X-CSRF-TOKEN", professionalAvailabilityRequestToken!);
        professionalAvailabilityPut.Content = JsonContent.Create(new
        {
            intervals = new[] { new { isoDayOfWeek = 1, startTime = "09:00", endTime = "17:00" } }
        });
        using var professionalAvailabilityResponse = await assignedAssignedProfessionalClient.SendAsync(professionalAvailabilityPut);
        Ensure(professionalAvailabilityResponse.StatusCode == HttpStatusCode.Forbidden,
            "Professional can view but cannot change their recurring availability; management remains Owner/Manager-only.");
    }

    Ensure((await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/professionals/eligible-members")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/services")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/customers")).StatusCode == HttpStatusCode.Forbidden &&
           (await assignedAssignedProfessionalClient.GetAsync("/api/v1/business-hours")).StatusCode == HttpStatusCode.Forbidden,
        "Professional cannot list other profiles, discover eligible members, access the unscoped service catalogue, customer directory or salon business-hours configuration.");

    var professionalAudit = await ReadProfessionalAuditAsync(
        migratorConnectionString, ownerTenantId, assignedAssignedProfessionalProfileId);
    Ensure(professionalAudit.Contains("professional.created") &&
           professionalAudit.Contains("professional_service.assigned") &&
           professionalAudit.Contains("professional_availability.created"),
        "Professional creation, service assignment and recurring availability are durably audited.");

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
    promotionFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    roleMatrixFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    using var sequentialLockoutFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    using var lockoutFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    using var crossFlowFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    using var revocationFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
    var raceDirectLoginEmail = "http-race-direct-login-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var raceSetupEmail = "http-race-setup-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var disableUpdateFailureEmail = "http-disable-update-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var revokeUpdateFailureEmail = "http-revoke-update-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var disableAuditFailureEmail = "http-disable-audit-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var revokeAuditFailureEmail = "http-revoke-audit-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var disableResultFailureEmail = "http-disable-result-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    var revokeResultFailureEmail = "http-revoke-result-failure-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    const string racePassword = "Atendee-InFlight-Race-2026!";
    Guid raceLoginUserId;
    Guid raceChallengeUserId;
    Guid raceEnrollmentUserId;
    Guid raceDirectLoginUserId;
    Guid raceSetupUserId;
    Guid disableUpdateFailureUserId;
    Guid revokeUpdateFailureUserId;
    Guid disableAuditFailureUserId;
    Guid revokeAuditFailureUserId;
    Guid disableResultFailureUserId;
    Guid revokeResultFailureUserId;
    using var raceFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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
        web.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserStore<OperationalIdentityUser>>();
            services.AddScoped<IUserStore<OperationalIdentityUser>, FailureInjectingOperationalIdentityUserStore>();
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
        Ensure(loginKey.Succeeded && loginMfa.Succeeded,
            "In-flight password-verification identity is MFA-enabled.");

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

        var directLoginUser = new OperationalIdentityUser
        {
            UserName = raceDirectLoginEmail,
            Email = raceDirectLoginEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var directLoginCreated = await users.CreateAsync(directLoginUser, racePassword);
        Ensure(directLoginCreated.Succeeded, "In-flight direct-login identity is created.");
        raceDirectLoginUserId = directLoginUser.Id;

        var setupUser = new OperationalIdentityUser
        {
            UserName = raceSetupEmail,
            Email = raceSetupEmail,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var setupCreated = await users.CreateAsync(setupUser, racePassword);
        Ensure(setupCreated.Succeeded, "In-flight MFA setup identity is created.");
        raceSetupUserId = setupUser.Id;

        async Task<Guid> CreateAccountSecurityFailureIdentityAsync(string email, string label)
        {
            var user = new OperationalIdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true
            };
            var created = await users.CreateAsync(user, racePassword);
            Ensure(created.Succeeded, label + " identity is created.");
            return user.Id;
        }

        disableUpdateFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            disableUpdateFailureEmail, "Disable-update-failure");
        revokeUpdateFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            revokeUpdateFailureEmail, "Revoke-update-failure");
        disableAuditFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            disableAuditFailureEmail, "Disable-audit-failure");
        revokeAuditFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            revokeAuditFailureEmail, "Revoke-audit-failure");
        disableResultFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            disableResultFailureEmail, "Disable-IdentityResult-failure");
        revokeResultFailureUserId = await CreateAccountSecurityFailureIdentityAsync(
            revokeResultFailureEmail, "Revoke-IdentityResult-failure");
    }
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceLoginUserId, roleId: 2);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceChallengeUserId, roleId: 2);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceEnrollmentUserId, roleId: 2);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceDirectLoginUserId, roleId: 3);
    await SeedMembershipAsync(migratorConnectionString, Guid.NewGuid(), raceSetupUserId, roleId: 2);

    using var raceLoginClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceLoginToken = await GetAntiforgeryRequestTokenAsync(raceLoginClient);
    // Pause the mandatory pending-MFA audit after password verification has
    // committed its identity lock, but before the challenge cookie is emitted.
    // Revocation must win this final issuance window: the subsequently emitted
    // challenge ticket carries the pre-revocation stamp and cannot be used.
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceLoginUserId,
        "operational_auth.password_verified_mfa_pending", enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var loginTask = PostWithCsrfAsync(raceLoginClient, "/api/v1/auth/login", raceLoginToken,
            new { email = raceLoginEmail, password = racePassword });
        try
        {
            await WaitForSecurityAuditDelayAsync(migratorConnectionString);
            await using var scope = raceFactory.Services.CreateAsyncScope();
            var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
            Ensure(await security.RevokeSessionsAsync(raceLoginUserId),
                "Session revocation commits after password verification but before challenge-cookie issuance.");
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await loginTask;
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : Array.Empty<string>();
            Ensure(response.StatusCode == HttpStatusCode.Accepted &&
                   cookies.Any(value => value.StartsWith("__Host-Atendee.MfaChallenge=", StringComparison.Ordinal)),
                "The in-flight password response emits a challenge cookie bound to the pre-revocation stamp.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceLoginUserId,
        "operational_auth.password_verified_mfa_pending", enabled: false);
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

    // Also cover the non-MFA direct-login branch: a normal Receptionist can
    // finish password verification, then be disabled while the success audit
    // is pending, before SignInWithClaimsAsync emits the application cookie.
    using var raceDirectLoginClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceDirectLoginToken = await GetAntiforgeryRequestTokenAsync(raceDirectLoginClient);
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceDirectLoginUserId,
        "operational_auth.login_succeeded", enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var directLoginTask = PostWithCsrfAsync(raceDirectLoginClient, "/api/v1/auth/login",
            raceDirectLoginToken, new { email = raceDirectLoginEmail, password = racePassword });
        try
        {
            await WaitForSecurityAuditDelayAsync(migratorConnectionString);
            await using var scope = raceFactory.Services.CreateAsyncScope();
            var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
            Ensure(await security.DisableAsync(raceDirectLoginUserId),
                "Account disablement commits after password verification but before direct application-cookie issuance.");
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await directLoginTask;
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : Array.Empty<string>();
            Ensure(response.StatusCode == HttpStatusCode.OK &&
                   cookies.Any(value => value.StartsWith("__Host-Atendee.Auth=", StringComparison.Ordinal)),
                "The in-flight direct-login response emits an application cookie bound to the pre-disable security stamp.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceDirectLoginUserId,
        "operational_auth.login_succeeded", enabled: false);
    Ensure((await raceDirectLoginClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "An application cookie emitted by direct login racing with account disablement is unusable.");

    // The privileged-role setup branch must also fail closed when the account
    // is disabled after password verification but before its initial setup ticket.
    using var raceSetupClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
    });
    var raceSetupLoginToken = await GetAntiforgeryRequestTokenAsync(raceSetupClient);
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceSetupUserId,
        "operational_auth.password_verified_mfa_pending", enabled: true);
    await using (var auditDelayLock = await AcquireSecurityAuditDelayLockAsync(migratorConnectionString))
    {
        var setupLoginTask = PostWithCsrfAsync(raceSetupClient, "/api/v1/auth/login",
            raceSetupLoginToken, new { email = raceSetupEmail, password = racePassword });
        try
        {
            await WaitForSecurityAuditDelayAsync(migratorConnectionString);
            await using var scope = raceFactory.Services.CreateAsyncScope();
            var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
            Ensure(await security.DisableAsync(raceSetupUserId),
                "Account disablement commits after password verification but before initial MFA setup-ticket issuance.");
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
            using var response = await setupLoginTask;
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : Array.Empty<string>();
            Ensure(response.StatusCode == HttpStatusCode.Accepted &&
                   cookies.Any(value => value.StartsWith("__Host-Atendee.MfaSetup=", StringComparison.Ordinal)),
                "The in-flight setup response emits a ticket bound to the pre-disable security stamp.");
        }
        finally
        {
            await ReleaseSecurityAuditDelayLockAsync(auditDelayLock);
        }
    }
    await SetSecurityAuditDelayTriggerAsync(migratorConnectionString, raceSetupUserId,
        "operational_auth.password_verified_mfa_pending", enabled: false);
    var raceSetupPostToken = await GetAntiforgeryRequestTokenAsync(raceSetupClient);
    using (var response = await PostWithCsrfAsync(raceSetupClient, "/api/v1/auth/mfa/enrollment/start",
        raceSetupPostToken, new { }))
        Ensure(response.StatusCode == HttpStatusCode.Unauthorized,
            "An initial MFA setup ticket emitted after account disablement cannot initialize enrollment.");
    Ensure((await raceSetupClient.GetAsync("/api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
        "The disabled setup identity cannot establish an application session.");

    // Security actions are transactional across the Identity mutation and
    // mandatory audit event. Force failures at both boundaries and prove the
    // previous lockout/security-stamp state survives without a partial audit row.
    string disableUpdateOriginalStamp;
    string revokeUpdateOriginalStamp;
    string disableAuditOriginalStamp;
    string revokeAuditOriginalStamp;
    string disableResultOriginalStamp;
    string revokeResultOriginalStamp;
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var disableUpdateUser = await users.FindByIdAsync(disableUpdateFailureUserId.ToString());
        var revokeUpdateUser = await users.FindByIdAsync(revokeUpdateFailureUserId.ToString());
        var disableAuditUser = await users.FindByIdAsync(disableAuditFailureUserId.ToString());
        var revokeAuditUser = await users.FindByIdAsync(revokeAuditFailureUserId.ToString());
        var disableResultUser = await users.FindByIdAsync(disableResultFailureUserId.ToString());
        var revokeResultUser = await users.FindByIdAsync(revokeResultFailureUserId.ToString());
        Ensure(disableUpdateUser is not null && revokeUpdateUser is not null &&
               disableAuditUser is not null && revokeAuditUser is not null &&
               disableResultUser is not null && revokeResultUser is not null,
            "All account-security failure identities remain available.");
        disableUpdateOriginalStamp = disableUpdateUser!.SecurityStamp!;
        revokeUpdateOriginalStamp = revokeUpdateUser!.SecurityStamp!;
        disableAuditOriginalStamp = disableAuditUser!.SecurityStamp!;
        revokeAuditOriginalStamp = revokeAuditUser!.SecurityStamp!;
        disableResultOriginalStamp = disableResultUser!.SecurityStamp!;
        revokeResultOriginalStamp = revokeResultUser!.SecurityStamp!;
    }

    await SetIdentityMutationFailureTriggerAsync(
        migratorConnectionString, disableUpdateFailureUserId, "lockout", enabled: true);
    var disableUpdateFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.DisableAsync(disableUpdateFailureUserId);
    }
    catch (Exception exception) when (exception is DbUpdateException or PostgresException)
    {
        disableUpdateFailed = true;
    }
    finally
    {
        await SetIdentityMutationFailureTriggerAsync(
            migratorConnectionString, disableUpdateFailureUserId, "lockout", enabled: false);
    }
    Ensure(disableUpdateFailed,
        "An Identity lockout-state update failure aborts account disablement.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(disableUpdateFailureUserId.ToString());
        Ensure(user is not null && user.LockoutEnd is null &&
               user.SecurityStamp == disableUpdateOriginalStamp,
            "A failed account-disable Identity update rolls back lockout and security-stamp mutations.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, disableUpdateFailureUserId))
               .Count(type => type == "operational_account.disabled") == 0,
        "A failed account-disable Identity update writes no audit event.");

    await SetIdentityMutationFailureTriggerAsync(
        migratorConnectionString, revokeUpdateFailureUserId, "stamp", enabled: true);
    var revokeUpdateFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.RevokeSessionsAsync(revokeUpdateFailureUserId);
    }
    catch (Exception exception) when (exception is DbUpdateException or PostgresException)
    {
        revokeUpdateFailed = true;
    }
    finally
    {
        await SetIdentityMutationFailureTriggerAsync(
            migratorConnectionString, revokeUpdateFailureUserId, "stamp", enabled: false);
    }
    Ensure(revokeUpdateFailed,
        "An Identity security-stamp update failure aborts session revocation.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(revokeUpdateFailureUserId.ToString());
        Ensure(user is not null && user.SecurityStamp == revokeUpdateOriginalStamp,
            "A failed session-revocation Identity update preserves the original security stamp.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, revokeUpdateFailureUserId))
               .Count(type => type == "operational_account.sessions_revoked") == 0,
        "A failed session-revocation Identity update writes no audit event.");

    await SetSecurityAuditFailureTriggerAsync(migratorConnectionString, disableAuditFailureUserId,
        "operational_account.disabled", enabled: true);
    var disableAuditFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.DisableAsync(disableAuditFailureUserId);
    }
    catch (Exception exception) when (exception is DbUpdateException or PostgresException)
    {
        disableAuditFailed = true;
    }
    finally
    {
        await SetSecurityAuditFailureTriggerAsync(migratorConnectionString, disableAuditFailureUserId,
            "operational_account.disabled", enabled: false);
    }
    Ensure(disableAuditFailed,
        "A mandatory account-disable audit failure aborts the security action.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(disableAuditFailureUserId.ToString());
        Ensure(user is not null && user.LockoutEnd is null &&
               user.SecurityStamp == disableAuditOriginalStamp,
            "An account-disable audit failure rolls back lockout and security-stamp mutations.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, disableAuditFailureUserId))
               .Count(type => type == "operational_account.disabled") == 0,
        "A rejected account-disable audit event is not persisted.");

    await SetSecurityAuditFailureTriggerAsync(migratorConnectionString, revokeAuditFailureUserId,
        "operational_account.sessions_revoked", enabled: true);
    var revokeAuditFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.RevokeSessionsAsync(revokeAuditFailureUserId);
    }
    catch (Exception exception) when (exception is DbUpdateException or PostgresException)
    {
        revokeAuditFailed = true;
    }
    finally
    {
        await SetSecurityAuditFailureTriggerAsync(migratorConnectionString, revokeAuditFailureUserId,
            "operational_account.sessions_revoked", enabled: false);
    }
    Ensure(revokeAuditFailed,
        "A mandatory session-revocation audit failure aborts the security action.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(revokeAuditFailureUserId.ToString());
        Ensure(user is not null && user.SecurityStamp == revokeAuditOriginalStamp,
            "A session-revocation audit failure rolls back the security-stamp mutation.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, revokeAuditFailureUserId))
               .Count(type => type == "operational_account.sessions_revoked") == 0,
        "A rejected session-revocation audit event is not persisted.");

    FailureInjectingOperationalIdentityUserStore.ForcedFailureUserId = disableResultFailureUserId;
    var disableIdentityResultFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.DisableAsync(disableResultFailureUserId);
    }
    catch (InvalidOperationException exception) when (
        exception.Message.StartsWith("Could not disable the operational identity.", StringComparison.Ordinal))
    {
        disableIdentityResultFailed = true;
    }
    finally
    {
        FailureInjectingOperationalIdentityUserStore.ForcedFailureUserId = null;
    }
    Ensure(disableIdentityResultFailed,
        "A failed IdentityResult from the UserManager store aborts account disablement before audit.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(disableResultFailureUserId.ToString());
        Ensure(user is not null && user.LockoutEnd is null &&
               user.SecurityStamp == disableResultOriginalStamp,
            "A failed IdentityResult during account disablement leaves persisted lockout and security stamp unchanged.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, disableResultFailureUserId))
               .Count(type => type == "operational_account.disabled") == 0,
        "A failed account-disable IdentityResult writes no audit event.");

    FailureInjectingOperationalIdentityUserStore.ForcedFailureUserId = revokeResultFailureUserId;
    var revokeIdentityResultFailed = false;
    try
    {
        await using var scope = raceFactory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<IOperationalAccountSecurityService>();
        await security.RevokeSessionsAsync(revokeResultFailureUserId);
    }
    catch (InvalidOperationException exception) when (
        exception.Message.StartsWith("Could not revoke existing operational sessions.", StringComparison.Ordinal))
    {
        revokeIdentityResultFailed = true;
    }
    finally
    {
        FailureInjectingOperationalIdentityUserStore.ForcedFailureUserId = null;
    }
    Ensure(revokeIdentityResultFailed,
        "A failed IdentityResult from the UserManager store aborts session revocation before audit.");
    await using (var scope = raceFactory.Services.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = await users.FindByIdAsync(revokeResultFailureUserId.ToString());
        Ensure(user is not null && user.SecurityStamp == revokeResultOriginalStamp,
            "A failed IdentityResult during session revocation leaves the persisted security stamp unchanged.");
    }
    Ensure((await ReadSecurityAuditTypesAsync(migratorConnectionString, revokeResultFailureUserId))
               .Count(type => type == "operational_account.sessions_revoked") == 0,
        "A failed session-revocation IdentityResult writes no audit event.");

    // Tenant lifecycle policy through real HTTP sessions. A fresh host gives these
    // logins their own per-IP login budget (4 of 5 permits are used here).
    statusKeyRing = Path.Combine(Path.GetTempPath(), "atendee-status-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(statusKeyRing);
    statusFactory = CreateIsolatedRateLimitFactory().WithWebHostBuilder(web =>
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


static WebApplicationFactory<Atendee.Api.ApiAssemblyMarker> CreateIsolatedRateLimitFactory() =>
    new IsolatedRateLimitWebApplicationFactory();
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

static async Task SetIdentityMutationFailureTriggerAsync(
    string connectionString,
    Guid targetUserId,
    string mutation,
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
            CREATE OR REPLACE FUNCTION identity.reject_account_security_mutation_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."Id" = '{targetUserId:D}'::uuid AND (
                    ('{mutation}' = 'lockout' AND NEW."LockoutEnd" IS DISTINCT FROM OLD."LockoutEnd")
                    OR
                    ('{mutation}' = 'stamp' AND NEW."SecurityStamp" IS DISTINCT FROM OLD."SecurityStamp")
                ) THEN
                    RAISE EXCEPTION 'Forced Identity mutation failure for account security HTTP integration test.';
                END IF;
                RETURN NEW;
            END;
            $function$;
            """;
        await using (var createFunction = new NpgsqlCommand(createFunctionSql, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectAccountSecurityMutation\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestRejectAccountSecurityMutation"
                BEFORE UPDATE ON identity."AspNetUsers"
                FOR EACH ROW EXECUTE FUNCTION identity.reject_account_security_mutation_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectAccountSecurityMutation\" ON identity.\"AspNetUsers\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.reject_account_security_mutation_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task SetSecurityAuditFailureTriggerAsync(
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
            CREATE OR REPLACE FUNCTION identity.reject_account_security_audit_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW."TargetIdentityUserId" = '{targetUserId:D}'::uuid
                   AND NEW."EventType" = '{eventType}' THEN
                    RAISE EXCEPTION 'Forced account-security audit failure for HTTP integration test.';
                END IF;
                RETURN NEW;
            END;
            $function$;
            """;
        await using (var createFunction = new NpgsqlCommand(createFunctionSql, connection, transaction))
            await createFunction.ExecuteNonQueryAsync();

        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectAccountSecurityAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var createTrigger = new NpgsqlCommand(
            """
            CREATE TRIGGER "TR_TestRejectAccountSecurityAudit"
                BEFORE INSERT ON identity."SecurityAuditEvents"
                FOR EACH ROW EXECUTE FUNCTION identity.reject_account_security_audit_for_test()
            """, connection, transaction);
        await createTrigger.ExecuteNonQueryAsync();
    }
    else
    {
        await using (var dropTrigger = new NpgsqlCommand(
            "DROP TRIGGER IF EXISTS \"TR_TestRejectAccountSecurityAudit\" ON identity.\"SecurityAuditEvents\"",
            connection, transaction))
            await dropTrigger.ExecuteNonQueryAsync();

        await using var dropFunction = new NpgsqlCommand(
            "DROP FUNCTION IF EXISTS identity.reject_account_security_audit_for_test()", connection, transaction);
        await dropFunction.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
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
              FROM pg_locks
             WHERE locktype = 'advisory'
               AND NOT granted
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
              FROM pg_locks
             WHERE locktype = 'transactionid'
               AND NOT granted
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

internal sealed class FailureInjectingOperationalIdentityUserStore(
    AtendeeIdentityDbContext context)
    : UserOnlyStore<OperationalIdentityUser, AtendeeIdentityDbContext, Guid>(context),
      IUserStore<OperationalIdentityUser>
{
    public static Guid? ForcedFailureUserId { get; set; }

    Task<IdentityResult> IUserStore<OperationalIdentityUser>.UpdateAsync(
        OperationalIdentityUser user,
        CancellationToken cancellationToken)
    {
        if (ForcedFailureUserId == user.Id)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "ForcedUserStoreUpdateFailure",
                Description = "Forced IdentityResult failure for account-security integration test."
            }));
        }

        return base.UpdateAsync(user, cancellationToken);
    }
}

internal sealed class IsolatedRateLimitWebApplicationFactory
    : WebApplicationFactory<Atendee.Api.ApiAssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var isolatedTestSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthenticationRateLimiting:PartitionKeySecret"] = isolatedTestSecret
            }));
    }
}