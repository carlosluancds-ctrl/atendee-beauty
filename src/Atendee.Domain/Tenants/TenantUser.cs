using Atendee.Domain.Primitives;

namespace Atendee.Domain.Tenants;

/// <summary>
/// Operational identity inside exactly one salon. Platform administrators
/// must use a separate identity model and are not represented by this type.
/// </summary>
public sealed class TenantUser
{
    private const int MaxDisplayNameLength = 120;

    private TenantUser(
        Guid id,
        TenantId tenantId,
        string displayName,
        TenantRole role,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        Role = role;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public Guid Id { get; }
    public TenantId TenantId { get; }
    public string DisplayName { get; }
    public TenantRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static TenantUser Create(
        TenantId tenantId,
        string displayName,
        TenantRole role,
        DateTimeOffset nowUtc)
    {
        if (!tenantId.IsValid)
        {
            throw new ArgumentException("A valid tenant ID is required.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length > MaxDisplayNameLength ||
            normalizedDisplayName.Any(char.IsControl))
        {
            throw new ArgumentException("Display name is invalid or too long.", nameof(displayName));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Creation timestamp must be UTC.", nameof(nowUtc));
        }

        return new TenantUser(Guid.NewGuid(), tenantId, normalizedDisplayName, role, nowUtc);
    }

    public void Deactivate() => IsActive = false;

    // Role changes require an authorized application use case and audit event.
}
