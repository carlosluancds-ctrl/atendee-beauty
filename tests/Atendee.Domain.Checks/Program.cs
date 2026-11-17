using Atendee.Domain.Primitives;
using Atendee.Domain.Tenants;

var nowUtc = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
var checks = new (string Name, Action Run)[]
{
    ("tenant name is trimmed", () => Assert(Tenant.Create("  Atendee Beauty  ", "atendee-beauty", "America/Sao_Paulo", nowUtc).Name == "Atendee Beauty")),
    ("slug is normalized", () => Assert(Tenant.Create("Salon", "Atendee-Beauty", "America/Sao_Paulo", nowUtc).Slug == "atendee-beauty")),
    ("new tenant starts pending verification", () => Assert(Tenant.Create("Salon", "salon", "America/Sao_Paulo", nowUtc).Status == TenantStatus.PendingVerification)),
    ("new tenant ID is non-empty", () => Assert(Tenant.Create("Salon", "salon", "America/Sao_Paulo", nowUtc).Id.IsValid)),
    ("repeated hyphens are rejected", () => Throws<ArgumentException>(() => Tenant.Create("Salon", "bad--slug", "America/Sao_Paulo", nowUtc))),
    ("unknown time zone is rejected", () => Throws<ArgumentException>(() => Tenant.Create("Salon", "salon", "Not/A_Real_Time_Zone", nowUtc))),
    ("non-UTC creation timestamp is rejected", () => Throws<ArgumentException>(() => Tenant.Create("Salon", "salon", "America/Sao_Paulo", nowUtc.ToOffset(TimeSpan.FromHours(-3))))),
    ("default empty TenantId is rejected", () => Throws<ArgumentException>(() => TenantUser.Create(default, "Owner", TenantRole.Owner, nowUtc))),
    ("undefined role is rejected", () => Throws<ArgumentOutOfRangeException>(() => TenantUser.Create(TenantId.New(), "Owner", (TenantRole)999, nowUtc))),
    ("overlong display name is rejected", () => Throws<ArgumentException>(() => TenantUser.Create(TenantId.New(), new string('x', 121), TenantRole.Professional, nowUtc))),
    ("control characters in display name are rejected", () => Throws<ArgumentException>(() => TenantUser.Create(TenantId.New(), "Bad\nName", TenantRole.Professional, nowUtc))),
    ("user display name is trimmed", () => Assert(TenantUser.Create(TenantId.New(), "  Ana  ", TenantRole.Professional, nowUtc).DisplayName == "Ana")),
    ("user is linked to the requested tenant", () => { var id = TenantId.New(); Assert(TenantUser.Create(id, "Ana", TenantRole.Professional, nowUtc).TenantId == id); }),
    ("new user is active", () => Assert(TenantUser.Create(TenantId.New(), "Ana", TenantRole.Professional, nowUtc).IsActive)),
    ("deactivation changes active state", () => { var user = TenantUser.Create(TenantId.New(), "Ana", TenantRole.Professional, nowUtc); user.Deactivate(); Assert(!user.IsActive); })
};

var failed = new List<string>();
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine("PASS " + check.Name);
    }
    catch (Exception exception)
    {
        failed.Add(check.Name + ": " + exception.Message);
        Console.WriteLine("FAIL " + failed[^1]);
    }
}

Console.WriteLine($"RESULT: {checks.Length - failed.Count}/{checks.Length} checks passed.");
if (failed.Count > 0)
    Environment.ExitCode = 1;

static void Assert(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException("Expected exception " + typeof(TException).Name + " was not thrown.");
}
