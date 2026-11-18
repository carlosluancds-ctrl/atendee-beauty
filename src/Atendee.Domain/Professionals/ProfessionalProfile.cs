namespace Atendee.Domain.Professionals;

/// <summary>
/// Tenant-owned profile that binds one operational identity with the Professional role.
/// Display name remains authoritative in the tenant membership record.
/// </summary>
public sealed record ProfessionalProfile(
    Guid Id,
    Guid IdentityUserId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ProfessionalProfile Create(
        Guid id,
        Guid identityUserId,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Professional profile ID is required.", nameof(id));
        if (identityUserId == Guid.Empty)
            throw new ArgumentException("Operational identity ID is required.", nameof(identityUserId));
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));

        return new ProfessionalProfile(id, identityUserId, true, nowUtc, nowUtc);
    }
}
