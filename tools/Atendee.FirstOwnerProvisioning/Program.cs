using Atendee.Infrastructure.Identity;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

try
{
    var options = ParseArguments(args);
    var connectionString = Environment.GetEnvironmentVariable(
        "ATENDEE_PROVISIONING_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            "ATENDEE_PROVISIONING_CONNECTION_STRING must be injected from the approved secret store.");

    var service = new FirstOwnerProvisioningService(connectionString);
    var result = await service.ProvisionAsync(new FirstOwnerProvisioningRequest(
        options.IdentityUserId,
        options.TenantName,
        options.TenantSlug,
        options.TimeZoneId,
        options.OwnerDisplayName));

    Console.WriteLine("First Owner provisioned successfully.");
    Console.WriteLine($"Tenant ID: {result.TenantId:D}");
    Console.WriteLine($"Tenant slug: {result.TenantSlug}");
    Console.WriteLine($"Owner identity ID: {result.OwnerIdentityUserId:D}");
    Console.WriteLine("Initial tenant status: PendingVerification");
    Console.WriteLine("The Owner must sign in and complete a fresh MFA challenge.");
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Npgsql.PostgresException)
{
    Console.Error.WriteLine($"Provisioning failed: {exception.Message}");
    return 1;
}

static Options ParseArguments(string[] values)
{
    var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < values.Length; index++)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal) ||
            index + 1 >= values.Length ||
            values[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Arguments must be supplied as --name value pairs.");

        var key = values[index][2..];
        if (key is not ("identity-user-id" or "tenant-name" or "tenant-slug" or "time-zone" or "owner-display-name") ||
            !parsed.TryAdd(key, values[++index]))
            throw new ArgumentException($"Unknown or repeated argument: --{key}.");
    }

    string Required(string key) =>
        parsed.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument missing: --{key}.");

    if (!Guid.TryParse(Required("identity-user-id"), out var identityId) || identityId == Guid.Empty)
        throw new ArgumentException("--identity-user-id must be a non-empty GUID.");

    return new Options(
        identityId,
        Required("tenant-name"),
        Required("tenant-slug"),
        Required("time-zone"),
        Required("owner-display-name"));
}

static void PrintUsage()
{
    Console.WriteLine("""
    Atendee Beauty - trusted first-Owner provisioning (offline only)

    Required environment:
      ATENDEE_PROVISIONING_CONNECTION_STRING
        Connection string for the dedicated atendee_migrator login.
        Inject it from a secret manager; do not put credentials in command history.

    Usage:
      dotnet run --project tools/Atendee.FirstOwnerProvisioning -- --identity-user-id <existing-guid> --tenant-name "<salon name>" --tenant-slug <unique-slug> --time-zone <time-zone> --owner-display-name "<owner display name>"

    The identity must already exist, have confirmed email, completed MFA,
    be unlocked, and have no existing salon membership. This command creates
    the tenant and Owner membership atomically. It never creates an identity.
    """);
}

internal sealed record Options(
    Guid IdentityUserId,
    string TenantName,
    string TenantSlug,
    string TimeZoneId,
    string OwnerDisplayName);
