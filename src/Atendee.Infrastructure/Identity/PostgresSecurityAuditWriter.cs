using System.Diagnostics;
using Atendee.Application.Abstractions;

namespace Atendee.Infrastructure.Identity;

public sealed class PostgresSecurityAuditWriter(AtendeeIdentityDbContext db) : ISecurityAuditWriter
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.Ordinal)
    {
        "operational_account.disabled",
        "operational_account.sessions_revoked"
    };

    public async Task WriteAsync(string eventType, Guid? actorIdentityUserId, Guid targetIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedEventTypes.Contains(eventType))
            throw new ArgumentOutOfRangeException(nameof(eventType), "Unsupported security audit event type.");
        if (targetIdentityUserId == Guid.Empty || actorIdentityUserId == Guid.Empty)
            throw new ArgumentException("Security audit identity identifiers must be non-empty.");

        var traceId = Activity.Current?.TraceId.ToString();
        db.SecurityAuditEvents.Add(new SecurityAuditEventRow
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            ActorIdentityUserId = actorIdentityUserId,
            TargetIdentityUserId = targetIdentityUserId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = string.IsNullOrWhiteSpace(traceId) ? null : traceId
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
