using System.Data.Common;
using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Infrastructure;
using Atendee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var rawConnectionString = Environment.GetEnvironmentVariable("ATENDEE_TEST_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(rawConnectionString))
{
    Console.WriteLine("SKIP: set ATENDEE_TEST_CONNECTION_STRING to a disposable PostgreSQL 18.7 database using atendee_app_login.");
    return;
}

var connectionString = new NpgsqlConnectionStringBuilder(rawConnectionString)
{
    Pooling = true,
    MinPoolSize = 0,
    MaxPoolSize = 1,
    Timeout = 5,
    CommandTimeout = 10
}.ConnectionString;

var tenantA = new TenantId(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
var tenantB = new TenantId(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
var noteA = Guid.NewGuid();
var attemptedCrossTenantNote = Guid.NewGuid();
var customerAId = Guid.NewGuid();
var attemptedCrossTenantCustomer = Guid.NewGuid();
var checks = 0;

var services = new ServiceCollection();
services.AddScoped<TestTenantAccessor>();
services.AddScoped<ICurrentTenantContext, TestCurrentTenantContext>();
services.AddScoped<ICurrentActorContext, TestCurrentActorContext>();
services.AddAtendeePersistence(connectionString);
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

int backendPidA;
await using (var scopeA = provider.CreateAsyncScope())
{
    SetTenant(scopeA.ServiceProvider, tenantA);
    var runner = scopeA.ServiceProvider.GetRequiredService<ITenantTransactionRunner>();
    var store = scopeA.ServiceProvider.GetRequiredService<ITenantNoteStore>();
    var customerStore = scopeA.ServiceProvider.GetRequiredService<ITenantCustomerStore>();
    var db = scopeA.ServiceProvider.GetRequiredService<TenantDbContext>();

    _ = await db.Database.SqlQueryRaw<string>(
        "SELECT set_config('app.actor_id', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', false) AS \"Value\"")
        .SingleAsync();
    var trustedActorSetting = await runner.ExecuteAsync(ct =>
        db.Database.SqlQueryRaw<string>("SELECT current_setting('app.actor_id', true) AS \"Value\"")
            .SingleAsync(ct));
    Check(trustedActorSetting == "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        "transaction runner clears a stale session actor and applies only the server-side actor context");

    await runner.ExecuteAsync(ct => store.AddAsync(noteA, "synthetic note from A", ct));
    var customer = await runner.ExecuteAsync(ct => customerStore.CreateAsync(
        "Maria Silva", "+55 (11) 99999-9999", "MARIA@EXAMPLE.COM", ct));
    customerAId = customer.Id;
    Check(customer.NormalizedPhoneNumber == "+5511999999999" && customer.EmailAddress == "maria@example.com",
        "customer store persists normalized tenant-owned contact details");
    var customerAuditCount = await runner.ExecuteAsync(ct => db.Database.SqlQuery<int>(
        $"SELECT count(*)::int AS \"Value\" FROM atendee.tenant_audit_events WHERE resource_id = {customerAId} AND event_type = 'customer.created'").SingleAsync(ct));
    Check(customerAuditCount == 1, "customer creation writes its audit event in the same tenant transaction");
    var visible = await runner.ExecuteAsync(ct => store.GetByIdAsync(noteA, ct));
    Check(visible is not null && visible.Body == "synthetic note from A", "tenant A can read its own note");
    backendPidA = await runner.ExecuteAsync(ct => db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(ct));
}

int backendPidB;
await using (var scopeB = provider.CreateAsyncScope())
{
    SetTenant(scopeB.ServiceProvider, tenantB);
    var runner = scopeB.ServiceProvider.GetRequiredService<ITenantTransactionRunner>();
    var store = scopeB.ServiceProvider.GetRequiredService<ITenantNoteStore>();
    var customerStore = scopeB.ServiceProvider.GetRequiredService<ITenantCustomerStore>();
    var db = scopeB.ServiceProvider.GetRequiredService<TenantDbContext>();

    backendPidB = await runner.ExecuteAsync(ct => db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(ct));
    Check(backendPidA == backendPidB, "connection pool reused the same PostgreSQL backend for A and B");

    var hidden = await runner.ExecuteAsync(ct => store.GetByIdAsync(noteA, ct));
    Check(hidden is null, "tenant B cannot read tenant A note by known UUID");
    var hiddenCustomer = await runner.ExecuteAsync(ct => customerStore.GetByIdAsync(customerAId, ct));
    var tenantBCustomers = await runner.ExecuteAsync(ct => customerStore.ListAsync(1, 50, ct));
    Check(hiddenCustomer is null && tenantBCustomers.TotalCount == 0 && tenantBCustomers.Items.Count == 0,
        "tenant B cannot read or list tenant A customers even when the customer UUID is known");
    var tenantBNotes = await runner.ExecuteAsync(ct => store.ListAsync(ct));
    Check(tenantBNotes.Count == 0, "tenant B list does not expose tenant A note");

    await ExpectDbFailureAsync(async () =>
    {
        await runner.ExecuteAsync(async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO atendee.tenant_notes (tenant_id, note_id, body, created_at_utc) VALUES ({tenantA.Value}, {attemptedCrossTenantNote}, {"cross tenant attempt"}, {DateTimeOffset.UtcNow})",
                ct);
        });
    });
    Check(true, "RLS WITH CHECK blocks cross-tenant insert");

    await ExpectDbFailureAsync(async () =>
    {
        await runner.ExecuteAsync(async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO atendee.customers (tenant_id, customer_id, full_name, normalized_phone, email_address, is_archived, created_at_utc, updated_at_utc) VALUES ({tenantA.Value}, {attemptedCrossTenantCustomer}, {"Forged customer"}, {"+5511888888888"}, {"forged@example.invalid"}, {false}, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})",
                ct);
        });
    });
    Check(true, "customer RLS WITH CHECK blocks a cross-tenant insert");

    await ExpectDbFailureAsync(async () =>
    {
        await db.Database.ExecuteSqlRawAsync("SELECT note_id FROM atendee.tenant_notes");
    });
    Check(true, "app login has no direct table access outside transaction runner");
    await ExpectDbFailureAsync(async () =>
    {
        await db.Database.ExecuteSqlRawAsync("SELECT customer_id FROM atendee.customers");
    });
    Check(true, "runtime login cannot bypass the tenant transaction runner to read customer PII");

    await ExpectFailureAsync(async () =>
    {
        await runner.ExecuteAsync<int>(_ => Task.FromException<int>(new InvalidOperationException("synthetic rollback test")));
    });
    Check(true, "failed operation rolls back and releases the transaction");
}

await using (var scopeA2 = provider.CreateAsyncScope())
{
    SetTenant(scopeA2.ServiceProvider, tenantA);
    var runner = scopeA2.ServiceProvider.GetRequiredService<ITenantTransactionRunner>();
    var store = scopeA2.ServiceProvider.GetRequiredService<ITenantNoteStore>();
    var customerStore = scopeA2.ServiceProvider.GetRequiredService<ITenantCustomerStore>();
    var db = scopeA2.ServiceProvider.GetRequiredService<TenantDbContext>();
    var backendPidA2 = await runner.ExecuteAsync(ct => db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(ct));
    Check(backendPidA2 == backendPidB, "same physical connection is safely reused after rollback");

    var visible = await runner.ExecuteAsync(ct => store.GetByIdAsync(noteA, ct));
    Check(visible is not null, "tenant A still sees its note after B and rollback tests");
    var visibleCustomer = await runner.ExecuteAsync(ct => customerStore.GetByIdAsync(customerAId, ct));
    Check(visibleCustomer is not null && !visibleCustomer.IsArchived,
        "tenant A retains its customer after tenant B and rollback tests");
    var archivedCustomer = await runner.ExecuteAsync(ct => customerStore.ArchiveAsync(customerAId, ct));
    Check(archivedCustomer is { IsArchived: true }, "customer archive is persisted as a soft state change");
    var archivedAuditCount = await runner.ExecuteAsync(ct => db.Database.SqlQuery<int>(
        $"SELECT count(*)::int AS \"Value\" FROM atendee.tenant_audit_events WHERE resource_id = {customerAId} AND event_type = 'customer.archived'").SingleAsync(ct));
    Check(archivedAuditCount == 1, "customer archival writes one mandatory audit event");
    var repeatedArchive = await runner.ExecuteAsync(ct => customerStore.ArchiveAsync(customerAId, ct));
    Check(repeatedArchive?.UpdatedAtUtc == archivedCustomer!.UpdatedAtUtc,
        "repeated customer archival is idempotent and does not rewrite the audit timestamp");
}

Console.WriteLine($"RESULT: {checks}/{checks} persistence integration checks passed.");

void SetTenant(IServiceProvider scopedServices, TenantId tenantId)
    => scopedServices.GetRequiredService<TestTenantAccessor>().TenantId = tenantId;

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    checks++;
    Console.WriteLine("PASS " + description);
}

static async Task ExpectDbFailureAsync(Func<Task> action)
{
    try { await action(); }
    catch (DbException) { return; }
    throw new InvalidOperationException("Expected a database authorization/RLS exception.");
}

static async Task ExpectFailureAsync(Func<Task> action)
{
    try { await action(); }
    catch (InvalidOperationException exception) when (exception.Message == "synthetic rollback test") { return; }
    throw new InvalidOperationException("Expected the synthetic operation failure.");
}

internal sealed class TestTenantAccessor
{
    public TenantId TenantId { get; set; }
}

internal sealed class TestCurrentTenantContext(TestTenantAccessor accessor) : ICurrentTenantContext
{
    public TenantId RequireTenantId() => accessor.TenantId;
}

internal sealed class TestCurrentActorContext : ICurrentActorContext
{
    public Guid? ActorId => Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    public bool IsPlatformAdministrator => false;
}
