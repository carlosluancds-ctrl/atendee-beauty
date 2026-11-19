using Atendee.Domain.Primitives;
using Atendee.Domain.Catalog;
using Atendee.Domain.Customers;
using Atendee.Domain.Scheduling;
using Atendee.Domain.Tenants;

var nowUtc = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
var checks = new (string Name, Action Run)[]
{
    ("customer name, E.164 phone and email are normalized", () =>
    {
        var customer = Customer.Create(Guid.NewGuid(), "  Maria Silva  ", "+55 (11) 99999-9999", "MARIA@EXAMPLE.COM", nowUtc);
        Assert(customer.FullName == "Maria Silva" && customer.NormalizedPhoneNumber == "+5511999999999" &&
               customer.EmailAddress == "maria@example.com" && !customer.IsArchived);
    }),
    ("customer requires at least one contact method", () =>
        Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), "Maria", null, null, nowUtc))),
    ("customer rejects non-E.164 phone numbers", () =>
        Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), "Maria", "11999999999", null, nowUtc))),
    ("customer rejects malformed email addresses", () =>
        Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), "Maria", null, "not-an-email", nowUtc))),
    ("customer rejects control characters in names", () =>
        Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), "Maria\nSilva", null, "maria@example.com", nowUtc))),
    ("customer update preserves creation time and refreshes normalized contacts", () =>
    {
        var customer = Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc);
        var updated = customer.Update("Maria Silva", null, "MARIA@EXAMPLE.COM", nowUtc.AddMinutes(1));
        Assert(updated.CreatedAtUtc == nowUtc && updated.UpdatedAtUtc == nowUtc.AddMinutes(1) &&
               updated.NormalizedPhoneNumber is null && updated.EmailAddress == "maria@example.com");
    }),
    ("customer cannot remove every contact method", () =>
    {
        var customer = Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc);
        Throws<ArgumentException>(() => customer.Update("Maria", null, null, nowUtc.AddMinutes(1)));
    }),
    ("customer archive is a timestamped soft state change", () =>
    {
        var customer = Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc);
        var archived = customer.Archive(nowUtc.AddMinutes(1));
        Assert(archived.IsArchived && archived.CreatedAtUtc == nowUtc && archived.UpdatedAtUtc == nowUtc.AddMinutes(1));
    }),
    ("repeated customer archive is idempotent", () =>
    {
        var archived = Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc)
            .Archive(nowUtc.AddMinutes(1));
        var repeated = archived.Archive(nowUtc.AddMinutes(2));
        Assert(repeated.UpdatedAtUtc == archived.UpdatedAtUtc);
    }),
    ("archived customer cannot be updated", () =>
    {
        var archived = Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc)
            .Archive(nowUtc.AddMinutes(1));
        Throws<InvalidOperationException>(() =>
            archived.Update("Maria Silva", "+5511999999999", null, nowUtc.AddMinutes(2)));
    }),
    ("customer rejects non-UTC timestamps", () =>
        Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), "Maria", "+5511999999999", null, nowUtc.ToOffset(TimeSpan.FromHours(-3))))),
    ("business-hours schedule sorts intervals by ISO weekday and local start time", () =>
    {
        var schedule = BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(2, new TimeOnly(10, 0), new TimeOnly(16, 0)),
            new BusinessHoursInterval(1, new TimeOnly(13, 0), new TimeOnly(17, 0)),
            new BusinessHoursInterval(1, new TimeOnly(9, 0), new TimeOnly(12, 0))
        });
        Assert(schedule.Intervals.Count == 3 &&
               schedule.Intervals[0].StartTime == new TimeOnly(9, 0) &&
               schedule.Intervals[1].StartTime == new TimeOnly(13, 0) &&
               schedule.Intervals[2].IsoDayOfWeek == 2);
    }),
    ("business-hours schedule permits an empty all-closed week", () =>
        Assert(BusinessHoursSchedule.Create(Array.Empty<BusinessHoursInterval>()).Intervals.Count == 0)),
    ("business-hours schedule rejects invalid ISO weekday", () =>
        Throws<ArgumentOutOfRangeException>(() => BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(0, new TimeOnly(9, 0), new TimeOnly(12, 0))
        }))),
    ("business-hours schedule rejects zero-length intervals", () =>
        Throws<ArgumentException>(() => BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(1, new TimeOnly(9, 0), new TimeOnly(9, 0))
        }))),
    ("business-hours schedule rejects overnight intervals", () =>
        Throws<ArgumentException>(() => BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(1, new TimeOnly(22, 0), new TimeOnly(2, 0))
        }))),
    ("business-hours schedule rejects overlapping intervals", () =>
        Throws<ArgumentException>(() => BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(1, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new BusinessHoursInterval(1, new TimeOnly(11, 0), new TimeOnly(14, 0))
        }))),
    ("business-hours schedule allows adjacent intervals", () =>
        Assert(BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(1, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new BusinessHoursInterval(1, new TimeOnly(12, 0), new TimeOnly(17, 0))
        }).Intervals.Count == 2)),
    ("business-hours schedule limits each day to four intervals", () =>
        Throws<ArgumentException>(() => BusinessHoursSchedule.Create(new[]
        {
            new BusinessHoursInterval(1, new TimeOnly(8, 0), new TimeOnly(9, 0)),
            new BusinessHoursInterval(1, new TimeOnly(9, 0), new TimeOnly(10, 0)),
            new BusinessHoursInterval(1, new TimeOnly(10, 0), new TimeOnly(11, 0)),
            new BusinessHoursInterval(1, new TimeOnly(11, 0), new TimeOnly(12, 0)),
            new BusinessHoursInterval(1, new TimeOnly(12, 0), new TimeOnly(13, 0))
        }))),
    ("business-hours schedule caps a week at 28 intervals", () =>
        Throws<ArgumentException>(() => BusinessHoursSchedule.Create(
            Enumerable.Range(0, 29).Select(_ => new BusinessHoursInterval(
                1, new TimeOnly(9, 0), new TimeOnly(10, 0)))))),
    ("service name is trimmed and currency normalized", () => { var service = SalonService.Create(Guid.NewGuid(), "  Corte  ", 45, 7500, "brl", 5, 10, true, nowUtc); Assert(service.Name == "Corte" && service.Currency == "BRL"); }),
    ("service price is an integer minor-unit amount", () => Assert(SalonService.Create(Guid.NewGuid(), "Corte", 45, 7500, "BRL", 0, 0, true, nowUtc).PriceMinorUnits == 7500)),
    ("invalid ISO currency is rejected", () => Throws<ArgumentException>(() => SalonService.Create(Guid.NewGuid(), "Corte", 45, 7500, "ZZZ", 0, 0, true, nowUtc))),
    ("negative service price is rejected", () => Throws<ArgumentOutOfRangeException>(() => SalonService.Create(Guid.NewGuid(), "Corte", 45, -1, "BRL", 0, 0, true, nowUtc))),
    ("zero service duration is rejected", () => Throws<ArgumentOutOfRangeException>(() => SalonService.Create(Guid.NewGuid(), "Corte", 0, 7500, "BRL", 0, 0, true, nowUtc))),
    ("negative service buffer is rejected", () => Throws<ArgumentOutOfRangeException>(() => SalonService.Create(Guid.NewGuid(), "Corte", 45, 7500, "BRL", -1, 0, true, nowUtc))),
    ("service update preserves creation timestamp and changes update timestamp", () => { var service = SalonService.Create(Guid.NewGuid(), "Corte", 45, 7500, "BRL", 0, 0, true, nowUtc); var updated = service.Update("Corte premium", 60, 9000, "BRL", 5, 10, false, nowUtc.AddMinutes(1)); Assert(updated.CreatedAtUtc == nowUtc && updated.UpdatedAtUtc == nowUtc.AddMinutes(1) && !updated.IsActive); }),
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
    ("deactivation changes active state", () => { var user = TenantUser.Create(TenantId.New(), "Ana", TenantRole.Professional, nowUtc); user.Deactivate(); Assert(!user.IsActive); }),
    ("status policy matches the approved matrix for every status, role and capability", PolicyMatchesApprovedMatrix),
    ("status policy denies undefined status, undefined role and malformed capabilities", PolicyDeniesUndefinedInputs),
    ("closed tenant grants no capability to any role", () =>
    {
        foreach (var role in Enum.GetValues<TenantRole>())
            Assert(TenantAccessPolicy.CapabilitiesFor(TenantStatus.Closed, role) == TenantCapability.None);
    }),
    ("every granted capability set includes login", () =>
    {
        foreach (var status in Enum.GetValues<TenantStatus>())
        foreach (var role in Enum.GetValues<TenantRole>())
        {
            var granted = TenantAccessPolicy.CapabilitiesFor(status, role);
            Assert(granted == TenantCapability.None || granted.HasFlag(TenantCapability.Login));
        }
    })
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

// Independent restatement of the approved matrix (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md).
// Column order of the Y/- string: CompleteOnboarding, ReadData, WriteData, Automation,
// ManageTeamAndSettings, Billing, ExportData. Login is implied for the listed roles.
static void PolicyMatchesApprovedMatrix()
{
    var allRoles = new[] { TenantRole.Owner, TenantRole.Manager, TenantRole.Receptionist, TenantRole.Professional };
    var everyone = allRoles;
    var ownerOnly = new[] { TenantRole.Owner };
    var ownerAndManager = new[] { TenantRole.Owner, TenantRole.Manager };
    var capabilityOrder = new[]
    {
        TenantCapability.CompleteOnboarding,
        TenantCapability.ReadData,
        TenantCapability.WriteData,
        TenantCapability.Automation,
        TenantCapability.ManageTeamAndSettings,
        TenantCapability.Billing,
        TenantCapability.ExportData
    };

    var approved = new (TenantStatus Status, TenantRole[] LoginRoles, string Allowed)[]
    {
        (TenantStatus.PendingVerification, ownerOnly,       "Y------"),
        (TenantStatus.SetupRequired,       ownerAndManager, "YYY-YY-"),
        (TenantStatus.Trial,               everyone,        "-YYYYYY"),
        (TenantStatus.Active,              everyone,        "-YYYYYY"),
        (TenantStatus.PastDue,             everyone,        "-YYYYYY"),
        (TenantStatus.Suspended,           everyone,        "-Y---YY"),
        (TenantStatus.Closing,             ownerOnly,       "-Y---YY"),
        (TenantStatus.Closed,              Array.Empty<TenantRole>(), "-------")
    };

    Assert(approved.Length == Enum.GetValues<TenantStatus>().Length);
    Assert(approved.Select(row => row.Status).Distinct().Count() == approved.Length);

    foreach (var row in approved)
    foreach (var role in allRoles)
    {
        var canLogin = row.LoginRoles.Contains(role);
        Assert(TenantAccessPolicy.IsAllowed(row.Status, role, TenantCapability.Login) == canLogin);

        for (var index = 0; index < capabilityOrder.Length; index++)
        {
            var expected = canLogin && row.Allowed[index] == 'Y';
            Assert(TenantAccessPolicy.IsAllowed(row.Status, role, capabilityOrder[index]) == expected);
        }
    }
}

static void PolicyDeniesUndefinedInputs()
{
    Assert(!TenantAccessPolicy.IsAllowed((TenantStatus)99, TenantRole.Owner, TenantCapability.Login));
    Assert(!TenantAccessPolicy.IsAllowed(TenantStatus.Active, (TenantRole)99, TenantCapability.Login));
    Assert(!TenantAccessPolicy.IsAllowed(TenantStatus.Active, TenantRole.Owner, TenantCapability.None));
    Assert(!TenantAccessPolicy.IsAllowed(TenantStatus.Active, TenantRole.Owner,
        TenantCapability.Login | TenantCapability.ReadData));
    Assert(!TenantAccessPolicy.IsAllowed(TenantStatus.Active, TenantRole.Owner, (TenantCapability)(1 << 20)));
    Assert(!TenantAccessPolicy.IsAllowed(TenantStatus.Active, TenantRole.Owner, (TenantCapability)(-1)));
    Assert(TenantAccessPolicy.CapabilitiesFor((TenantStatus)99, TenantRole.Owner) == TenantCapability.None);
    Assert(TenantAccessPolicy.CapabilitiesFor(TenantStatus.Active, (TenantRole)0) == TenantCapability.None);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException("Expected exception " + typeof(TException).Name + " was not thrown.");
}
