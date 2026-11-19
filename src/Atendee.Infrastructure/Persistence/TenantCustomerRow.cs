using Atendee.Domain.Customers;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantCustomerRow
{
    private TenantCustomerRow() { } // EF Core

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? NormalizedPhoneNumber { get; private set; }
    public string? EmailAddress { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantCustomerRow FromDomain(Guid tenantId, Customer customer)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        return new TenantCustomerRow
        {
            TenantId = tenantId,
            Id = customer.Id,
            FullName = customer.FullName,
            NormalizedPhoneNumber = customer.NormalizedPhoneNumber,
            EmailAddress = customer.EmailAddress,
            IsArchived = customer.IsArchived,
            CreatedAtUtc = customer.CreatedAtUtc,
            UpdatedAtUtc = customer.UpdatedAtUtc
        };
    }

    public void Apply(Customer customer)
    {
        if (customer.Id != Id)
            throw new InvalidOperationException("Cannot change the identity of a persisted customer.");

        FullName = customer.FullName;
        NormalizedPhoneNumber = customer.NormalizedPhoneNumber;
        EmailAddress = customer.EmailAddress;
        IsArchived = customer.IsArchived;
        UpdatedAtUtc = customer.UpdatedAtUtc;
    }

    public Customer ToDomain() => new(
        Id,
        FullName,
        NormalizedPhoneNumber,
        EmailAddress,
        IsArchived,
        CreatedAtUtc,
        UpdatedAtUtc);
}
