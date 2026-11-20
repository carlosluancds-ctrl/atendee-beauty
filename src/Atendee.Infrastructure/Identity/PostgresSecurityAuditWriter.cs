using System.Diagnostics;
using Atendee.Application.Abstractions;

namespace Atendee.Infrastructure.Identity;

public sealed class PostgresSecurityAuditWriter(AtendeeIdentityDbContext db) : ISecurityAuditWriter
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.Ordinal)
    {
        "operational_account.disabled",
        "operational_account.sessions_revoked",
        "operational_mfa.enrolled",
        "operational_mfa.totp_challenge_succeeded",
        "operational_mfa.recovery_code_redeemed",
        "operational_mfa.challenge_failed",
        "operational_mfa.enrollment_failed",
        "operational_auth.login_succeeded",
        "operational_auth.login_failed",
        "operational_auth.password_verified_mfa_pending",
        "operational_identity.activation_issued",
        "operational_identity.activation_reissued",
        "operational_identity.activation_succeeded",
        "tenant_membership.created",
        "tenant_membership.role_changed",
        "tenant_membership.activated",
        "tenant_membership.deactivated"
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