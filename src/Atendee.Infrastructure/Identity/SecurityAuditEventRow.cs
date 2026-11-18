namespace Atendee.Infrastructure.Identity;

internal sealed class SecurityAuditEventRow
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid? ActorIdentityUserId { get; set; }
    public Guid TargetIdentityUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}
