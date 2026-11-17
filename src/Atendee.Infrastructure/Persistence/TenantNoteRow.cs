namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantNoteRow
{
    private TenantNoteRow() { } // EF Core

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TenantNoteRow Create(Guid tenantId, Guid id, string body, DateTimeOffset createdAtUtc)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (id == Guid.Empty) throw new ArgumentException("Record ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var normalizedBody = body.Trim();
        if (normalizedBody.Length > 1000 || normalizedBody.Any(char.IsControl))
            throw new ArgumentException("Body is invalid or exceeds 1000 characters.", nameof(body));
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(createdAtUtc));

        return new TenantNoteRow
        {
            TenantId = tenantId,
            Id = id,
            Body = normalizedBody,
            CreatedAtUtc = createdAtUtc
        };
    }
}
