using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Customers;
using Atendee.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantCustomerStore : ITenantCustomerStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantCustomerStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<CustomerPageModel> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = _db.TenantCustomers.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.FullName)
            .ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new CustomerModel(
                row.Id,
                row.FullName,
                row.NormalizedPhoneNumber,
                row.EmailAddress,
                row.IsArchived,
                row.CreatedAtUtc,
                row.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new CustomerPageModel(items, page, pageSize, totalCount);
    }

    public async Task<CustomerModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _db.TenantCustomers
            .AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new CustomerModel(
                row.Id,
                row.FullName,
                row.NormalizedPhoneNumber,
                row.EmailAddress,
                row.IsArchived,
                row.CreatedAtUtc,
                row.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CustomerModel> CreateAsync(
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var customer = Customer.Create(
            Guid.NewGuid(), fullName, phoneNumber, emailAddress, DateTimeOffset.UtcNow);

        _db.TenantCustomers.Add(TenantCustomerRow.FromDomain(_tenantId.Value, customer));
        return Task.FromResult(ToModel(customer));
    }

    public async Task<CustomerModel?> UpdateAsync(
        Guid id,
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantCustomers.SingleOrDefaultAsync(
            item => item.Id == id && !item.IsArchived, cancellationToken);
        if (row is null)
            return null;

        var updated = row.ToDomain().Update(
            fullName, phoneNumber, emailAddress, DateTimeOffset.UtcNow);
        row.Apply(updated);
        return ToModel(updated);
    }

    public async Task<CustomerModel?> ArchiveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantCustomers.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (row is null)
            return null;

        var archived = row.ToDomain().Archive(DateTimeOffset.UtcNow);
        row.Apply(archived);
        return ToModel(archived);
    }

    private static CustomerModel ToModel(Customer customer) => new(
        customer.Id,
        customer.FullName,
        customer.NormalizedPhoneNumber,
        customer.EmailAddress,
        customer.IsArchived,
        customer.CreatedAtUtc,
        customer.UpdatedAtUtc);
}
