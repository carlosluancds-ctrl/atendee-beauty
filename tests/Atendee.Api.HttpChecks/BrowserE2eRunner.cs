using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Atendee.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class BrowserE2eRunner
{
    public static async Task RunAsync(
        string identityConnectionString,
        string tenantConnectionString,
        string migratorConnectionString,
        string apiContentRoot,
        string portalDirectory)
    {
        var keyRing = Path.Combine(Path.GetTempPath(), "atendee-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyRing);

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));

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
            // .NET 10 WebApplicationFactory Kestrel mode gives the browser a real
            // network endpoint while preserving the production API middleware.
            factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            factory.StartServer();

            var server = factory.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var apiAddress = addresses?.SingleOrDefault(address =>
                address.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(apiAddress))
                throw new InvalidOperationException("The browser API host did not publish its Kestrel address.");

            (Guid Id, string Email, string Password, string? AuthenticatorKey) owner;
            (Guid Id, string Email, string Password, string? AuthenticatorKey) manager;
            (Guid Id, string Email, string Password, string? AuthenticatorKey) professional;
            (Guid Id, string Email, string Password, string? AuthenticatorKey) receptionist;
            (Guid Id, string Email, string Password, string? AuthenticatorKey) secondOwner;

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
                owner = await CreateUserAsync(users, "browser-owner-", "Atendee-E2E-Owner-2026!", enableMfa: true);
                manager = await CreateUserAsync(users, "browser-manager-", "Atendee-E2E-Manager-2026!", enableMfa: false);
                professional = await CreateUserAsync(users, "browser-professional-", "Atendee-E2E-Professional-2026!", enableMfa: false);
                receptionist = await CreateUserAsync(users, "browser-receptionist-", "Atendee-E2E-Receptionist-2026!", enableMfa: false);
                secondOwner = await CreateUserAsync(users, "browser-second-owner-", "Atendee-E2E-Second-Owner-2026!", enableMfa: true);
            }

            var tenantSlug = "browser-e2e-" + Guid.NewGuid().ToString("N");
            var provisioned = await new FirstOwnerProvisioningService(migratorConnectionString).ProvisionAsync(
                new FirstOwnerProvisioningRequest(
                    owner.Id, "Browser E2E Salon", tenantSlug, "America/Sao_Paulo", "Owner E2E"));
            await SetTenantActiveAsync(migratorConnectionString, provisioned.TenantId);
            await SeedMembershipAsync(migratorConnectionString, provisioned.TenantId, manager.Id, "Manager E2E", roleId: 2);
            await SeedMembershipAsync(migratorConnectionString, provisioned.TenantId, professional.Id, "Beatriz E2E", roleId: 4);
            await SeedMembershipAsync(migratorConnectionString, provisioned.TenantId, receptionist.Id, "Receptionist E2E", roleId: 3);

            var secondTenantSlug = "browser-e2e-second-" + Guid.NewGuid().ToString("N");
            var secondProvisioned = await new FirstOwnerProvisioningService(migratorConnectionString).ProvisionAsync(
                new FirstOwnerProvisioningRequest(
                    secondOwner.Id, "Browser E2E Salon B", secondTenantSlug, "America/Sao_Paulo", "Second Owner E2E"));
            await SetTenantActiveAsync(migratorConnectionString, secondProvisioned.TenantId);

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
                WorkingDirectory = portalDirectory,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("test:e2e:fullstack");
            Console.WriteLine("Starting full-stack browser E2E against the temporary HTTPS Kestrel API.");
            foreach (var name in startInfo.Environment.Keys
                .Where(name => name.StartsWith("ATENDEE_TEST_", StringComparison.Ordinal) ||
                               name.StartsWith("ConnectionStrings__", StringComparison.Ordinal) ||
                               name is "DataProtection__KeyRingPath" or "ATENDEE_TEST_POSTGRES_ADMIN_PASSWORD")
                .ToArray())
            {
                startInfo.Environment.Remove(name);
            }

            startInfo.Environment["ATENDEE_API_PROXY_TARGET"] = apiAddress;
            startInfo.Environment["ATENDEE_E2E_OWNER_EMAIL"] = owner.Email;
            startInfo.Environment["ATENDEE_E2E_OWNER_PASSWORD"] = owner.Password;
            startInfo.Environment["ATENDEE_E2E_OWNER_TOTP_SECRET"] = owner.AuthenticatorKey!;
            startInfo.Environment["ATENDEE_E2E_MANAGER_EMAIL"] = manager.Email;
            startInfo.Environment["ATENDEE_E2E_MANAGER_PASSWORD"] = manager.Password;
            startInfo.Environment["ATENDEE_E2E_PROFESSIONAL_EMAIL"] = professional.Email;
            startInfo.Environment["ATENDEE_E2E_PROFESSIONAL_PASSWORD"] = professional.Password;
            startInfo.Environment["ATENDEE_E2E_PROFESSIONAL_ID"] = professional.Id.ToString("D");
            startInfo.Environment["ATENDEE_E2E_RECEPTIONIST_EMAIL"] = receptionist.Email;
            startInfo.Environment["ATENDEE_E2E_RECEPTIONIST_PASSWORD"] = receptionist.Password;
            startInfo.Environment["ATENDEE_E2E_SECOND_OWNER_EMAIL"] = secondOwner.Email;
            startInfo.Environment["ATENDEE_E2E_SECOND_OWNER_PASSWORD"] = secondOwner.Password;
            startInfo.Environment["ATENDEE_E2E_SECOND_OWNER_TOTP_SECRET"] = secondOwner.AuthenticatorKey!;

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the full-stack Playwright process.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Full-stack browser E2E exceeded its five-minute limit.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException("Full-stack browser E2E failed with exit code " + process.ExitCode + ".");
            Console.WriteLine("RESULT: Full-stack browser E2E passed against the real Kestrel API and disposable PostgreSQL database.");
        }
        finally
        {
            await factory.DisposeAsync();
            try
            {
                if (Directory.Exists(keyRing))
                    Directory.Delete(keyRing, recursive: true);
            }
            catch
            {
                // Temporary test key cleanup must not hide the browser-test result.
            }
        }
    }

    private static async Task<(Guid Id, string Email, string Password, string? AuthenticatorKey)> CreateUserAsync(
        UserManager<OperationalIdentityUser> users,
        string prefix,
        string password,
        bool enableMfa)
    {
        var email = prefix + Guid.NewGuid().ToString("N") + "@example.invalid";
        var user = new OperationalIdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException("Could not create synthetic browser identity: " +
                string.Join("; ", created.Errors.Select(error => error.Code)));

        string? authenticatorKey = null;
        if (enableMfa)
        {
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded)
                throw new InvalidOperationException("Could not initialize the synthetic Owner authenticator key.");
            authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(authenticatorKey))
                throw new InvalidOperationException("The synthetic Owner authenticator key is empty.");
            var enabled = await users.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
                throw new InvalidOperationException("Could not enable MFA for the synthetic Owner identity.");
        }

        return (user.Id, email, password, authenticatorKey);
    }

    private static async Task SetTenantActiveAsync(string connectionString, Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
            await role.ExecuteNonQueryAsync();
        await using (var update = new NpgsqlCommand(
            "UPDATE atendee.tenants SET status = 3 WHERE id = @tenant", connection, transaction))
        {
            update.Parameters.AddWithValue("tenant", tenantId);
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Could not activate the synthetic browser-test tenant.");
        }
        await transaction.CommitAsync();
    }

    private static async Task SeedMembershipAsync(
        string connectionString, Guid tenantId, Guid userId, string displayName, short roleId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, transaction))
            await role.ExecuteNonQueryAsync();
        await using (var membership = new NpgsqlCommand(
            """
            INSERT INTO atendee.tenant_users
                (tenant_id, identity_user_id, display_name, role, is_active, created_at_utc)
            VALUES (@tenant, @user, @display_name, @role, true, now())
            """, connection, transaction))
        {
            membership.Parameters.AddWithValue("tenant", tenantId);
            membership.Parameters.AddWithValue("user", userId);
            membership.Parameters.AddWithValue("display_name", displayName);
            membership.Parameters.AddWithValue("role", roleId);
            await membership.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
