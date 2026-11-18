namespace Atendee.Infrastructure.Identity;

internal sealed class OperationalIdentityActivationTokenRow
{
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}