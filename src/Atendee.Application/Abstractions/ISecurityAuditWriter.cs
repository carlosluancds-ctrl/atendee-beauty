namespace Atendee.Application.Abstractions;

public interface ISecurityAuditWriter
{
    Task WriteAsync(string eventType, Guid? actorIdentityUserId, Guid targetIdentityUserId, CancellationToken cancellationToken = default);
}
