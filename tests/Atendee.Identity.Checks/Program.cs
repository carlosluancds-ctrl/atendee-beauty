using System.Buffers.Binary;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Atendee.Application.Abstractions;
using Atendee.Domain.Primitives;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var identityConnectionString = Environment.GetEnvironmentVariable("ATENDEE_TEST_IDENTITY_CONNECTION_STRING");
var migratorConnectionString = Environment.GetEnvironmentVariable("ATENDEE_TEST_MIGRATOR_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(identityConnectionString) ||
    string.IsNullOrWhiteSpace(migratorConnectionString))
    throw new InvalidOperationException("Identity integration checks require disposable PostgreSQL connections; SKIP is not allowed.");

var keyRing = Path.Combine(Path.GetTempPath(), "atendee-identity-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(keyRing);
var services = new ServiceCollection();
services.AddLogging();
services.AddDataProtection().SetApplicationName("AtendeeBeauty").PersistKeysToFileSystem(new DirectoryInfo(keyRing));
services.AddAtendeeIdentity(identityConnectionString);
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}

try
{
    var userId = Guid.NewGuid();
    var tenantId = Guid.NewGuid();
    var secondTenantId = Guid.NewGuid();
    var email = "identity-check-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    string secret;
    string recoveryCode;

    await using (var scope = provider.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();
        var mappedTables = db.Model.GetEntityTypes()
            .Select(entity => (entity.GetSchema(), entity.GetTableName()))
            .Where(item => item.Item2 != null)
            .ToHashSet();

        Check(mappedTables.Contains(("identity", "AspNetUsers")) &&
              mappedTables.Contains(("identity", "AspNetUserClaims")) &&
              mappedTables.Contains(("identity", "AspNetUserLogins")) &&
              mappedTables.Contains(("identity", "AspNetUserTokens")),
              "Identity v2 maps its four tables to the identity schema");
        Check(!db.Model.GetEntityTypes().Any(entity =>
            entity.ClrType.FullName?.Contains("IdentityUserPasskey", StringComparison.Ordinal) == true),
            "Passkey schema is deferred until a reviewed migration");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var user = new OperationalIdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, "Atendee-Test-2026!");
        Check(created.Succeeded, "Synthetic operational identity is created");
        Check(await users.CheckPasswordAsync(user, "Atendee-Test-2026!"), "Correct password is accepted");
        Check(!await users.CheckPasswordAsync(user, "Wrong-password-2026!"), "Incorrect password is rejected");

        var reset = await users.ResetAuthenticatorKeyAsync(user);
        Check(reset.Succeeded, "TOTP authenticator key is initialized");
        secret = (await users.GetAuthenticatorKeyAsync(user))!;
        Check(!string.IsNullOrWhiteSpace(secret), "Authenticator key is returned in base32");

        var rawSecret = await ReadTokenAsync(identityConnectionString, user.Id, "[AspNetUserStore]", "AuthenticatorKey");
        Check(rawSecret != secret && !rawSecret.Contains(secret, StringComparison.Ordinal),
            "TOTP secret is encrypted at rest");
        Check(await users.GetAuthenticatorKeyAsync(user) == secret,
            "Identity transparently decrypts the TOTP secret");

        var code = CreateTotp(secret, DateTimeOffset.UtcNow);
        Check(await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code),
            "RFC 6238 TOTP is accepted by the replay-protected Identity provider");
        Check(!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code),
            "A previously accepted TOTP time step cannot be replayed");

        var nextStepCode = CreateTotp(secret, DateTimeOffset.UtcNow.AddSeconds(30));
        var concurrentResults = await Task.WhenAll(
            VerifyTotpInNewScopeAsync(provider, user.Id, nextStepCode),
            VerifyTotpInNewScopeAsync(provider, user.Id, nextStepCode));
        Check(concurrentResults.Count(result => result) == 1,
            "Concurrent requests cannot both consume the same TOTP time step");

        var enabled = await users.SetTwoFactorEnabledAsync(user, true);
        Check(enabled.Succeeded, "TOTP is enabled on the operational identity");

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        Check(codes != null && codes.Count() == 10, "Ten one-time recovery codes are generated");
        recoveryCode = codes!.First();

        var rawRecovery = await ReadRecoveryTokenAsync(identityConnectionString, user.Id);
        Check(!string.IsNullOrWhiteSpace(rawRecovery) &&
              !rawRecovery.Contains(recoveryCode, StringComparison.Ordinal),
              "Recovery codes are encrypted at rest");

        var redeemed = await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
        Check(redeemed.Succeeded, "Recovery code can be redeemed once");
        var replayed = await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
        Check(!replayed.Succeeded, "Redeemed recovery code cannot be replayed");
        userId = user.Id;
    }

    await SeedMembershipAsync(migratorConnectionString, tenantId, userId);
    await SeedSecondTenantAsync(migratorConnectionString, secondTenantId);

    await using (var scope = provider.CreateAsyncScope())
    {
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantMembershipResolver>();
        var membership = await resolver.ResolveAsync(userId);
        Check(membership != null &&
              membership.TenantId.Value == tenantId &&
              membership.Role == TenantRole.Owner &&
              membership.IsActive &&
              membership.TenantStatus == TenantStatus.Active,
              "Restricted resolver returns the user's single active salon membership");

        await AssertDirectMembershipReadDeniedAsync(identityConnectionString);
        Check(true, "Auth login cannot directly read tenant membership tables");

        var trusted = scope.ServiceProvider.GetRequiredService<TrustedRequestContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
        var middleware = new TenantContextMiddleware(_ =>
        {
            Check(trusted.RequireTenantId().Value == tenantId,
                "Trusted context ignores client-supplied tenant identifiers");
            Check(trusted.ActorId == userId && trusted.Role == TenantRole.Owner,
                "Actor and role come from the persisted membership");
            return Task.CompletedTask;
        });
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("atendee:tenant_id", secondTenantId.ToString()),
            new Claim("atendee:tenant_role", TenantRole.Professional.ToString()),
            new Claim("amr", "mfa")
        }, "test"));
        http.Request.Headers["X-Tenant-ID"] = secondTenantId.ToString();
        http.Request.QueryString = new QueryString("?tenantId=" + secondTenantId);
        await middleware.InvokeAsync(http, resolver, trusted, userManager);
        Check(http.User.FindFirst("atendee:tenant_id")?.Value == tenantId.ToString(),
            "Stale tenant claims are replaced from the database each request");
    }

    var duplicateBlocked = await TryInsertSecondMembershipAsync(migratorConnectionString, secondTenantId, userId);
    Check(duplicateBlocked, "Database prevents one operational identity from joining a second salon");
    Console.WriteLine($"RESULT: {checks}/{checks} Identity checks passed.");
}
finally
{
    try { if (Directory.Exists(keyRing)) Directory.Delete(keyRing, true); }
    catch { }
}

static async Task<bool> VerifyTotpInNewScopeAsync(
    ServiceProvider provider,
    Guid userId,
    string code)
{
    await using var scope = provider.CreateAsyncScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<OperationalIdentityUser>>();
    var user = await users.FindByIdAsync(userId.ToString());
    return user is not null &&
           await users.VerifyTwoFactorTokenAsync(
               user,
               users.Options.Tokens.AuthenticatorTokenProvider,
               code);
}

static async Task<string> ReadTokenAsync(string cs, Guid userId, string loginProvider, string name)
{
    await using var connection = new NpgsqlConnection(cs);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT "Value" FROM identity."AspNetUserTokens"
        WHERE "UserId"=@user AND "LoginProvider"=@provider AND "Name"=@name
        """, connection);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("provider", loginProvider);
    command.Parameters.AddWithValue("name", name);
    return (string)(await command.ExecuteScalarAsync())!;
}

static Task<string> ReadRecoveryTokenAsync(string cs, Guid userId)
    => ReadTokenAsync(cs, userId, "[AspNetUserStore]", "RecoveryCodes");

static async Task AssertDirectMembershipReadDeniedAsync(string cs)
{
    await using var connection = new NpgsqlConnection(cs);
    await connection.OpenAsync();
    try
    {
        await using var command = new NpgsqlCommand("SELECT tenant_id FROM atendee.tenant_users LIMIT 1", connection);
        await command.ExecuteScalarAsync();
        throw new InvalidOperationException("Auth login unexpectedly has direct SELECT on tenant_users.");
    }
    catch (PostgresException ex) when (ex.SqlState == "42501") { }

    try
    {
        await using var command = new NpgsqlCommand("SELECT id FROM atendee.tenants LIMIT 1", connection);
        await command.ExecuteScalarAsync();
        throw new InvalidOperationException("Auth login unexpectedly has direct SELECT on tenants.");
    }
    catch (PostgresException ex) when (ex.SqlState == "42501") { }

    await using var resolverCommand = new NpgsqlCommand(
        "SELECT count(*) FROM atendee.resolve_tenant_membership()", connection);
    var rows = Convert.ToInt64(await resolverCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    if (rows != 0)
        throw new InvalidOperationException("Membership resolver returned rows without transaction-local authenticated identity context.");
}

static async Task SeedMembershipAsync(string cs, Guid tenantId, Guid userId)
{
    await using var connection = new NpgsqlConnection(cs);
    await connection.OpenAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, tx))
        await role.ExecuteNonQueryAsync();

    await using (var tenant = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenants(id,name,slug,time_zone_id,status,created_at_utc)
        VALUES(@id,'Synthetic Salon','identity-' || @id::text,'America/Sao_Paulo',3,now())
        """,
        connection, tx))
    {
        tenant.Parameters.AddWithValue("id", tenantId);
        await tenant.ExecuteNonQueryAsync();
    }

    await using (var membership = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenant_users
            (tenant_id,identity_user_id,display_name,role,is_active,created_at_utc)
        VALUES(@tenant,@user,'Synthetic Owner',1,true,now())
        """, connection, tx))
    {
        membership.Parameters.AddWithValue("tenant", tenantId);
        membership.Parameters.AddWithValue("user", userId);
        await membership.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
}

static async Task SeedSecondTenantAsync(string cs, Guid tenantId)
{
    await using var connection = new NpgsqlConnection(cs);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO atendee.tenants(id,name,slug,time_zone_id,status,created_at_utc)
        VALUES(@id,'Second Synthetic Salon','second-' || @id::text,'America/Sao_Paulo',3,now())
        """, connection);
    command.Parameters.AddWithValue("id", tenantId);
    await command.ExecuteNonQueryAsync();
}

static async Task<bool> TryInsertSecondMembershipAsync(string cs, Guid tenantId, Guid userId)
{
    await using var connection = new NpgsqlConnection(cs);
    await connection.OpenAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using (var role = new NpgsqlCommand("SET LOCAL ROLE atendee_owner", connection, tx))
        await role.ExecuteNonQueryAsync();
    try
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO atendee.tenant_users
                (tenant_id,identity_user_id,display_name,role,is_active,created_at_utc)
            VALUES(@tenant,@user,'Second Owner',1,true,now())
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync();
        await tx.RollbackAsync();
        return false;
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        await tx.RollbackAsync();
        return true;
    }
}

static string CreateTotp(string base32, DateTimeOffset now)
{
    var key = DecodeBase32(base32);
    var counter = (ulong)(now.ToUnixTimeSeconds() / 30);
    Span<byte> message = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(message, counter);
#pragma warning disable CA5350
    var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
    var offset = hash[^1] & 15;
    var binary = ((hash[offset] & 127) << 24) |
                 ((hash[offset + 1] & 255) << 16) |
                 ((hash[offset + 2] & 255) << 8) |
                 (hash[offset + 3] & 255);
    return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}

static byte[] DecodeBase32(string value)
{
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    var bytes = new List<byte>();
    var buffer = 0;
    var bits = 0;
    foreach (var character in value.TrimEnd('=').ToUpperInvariant())
    {
        var item = alphabet.IndexOf(character);
        if (item < 0) throw new FormatException("Invalid base32 authenticator key.");
        buffer = (buffer << 5) | item;
        bits += 5;
        if (bits >= 8)
        {
            bits -= 8;
            bytes.Add((byte)((buffer >> bits) & 255));
        }
    }
    return bytes.ToArray();
}
