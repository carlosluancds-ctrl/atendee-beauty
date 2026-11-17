namespace Atendee.Domain.Primitives;

public readonly record struct TenantId
{
    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;

    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static TenantId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
