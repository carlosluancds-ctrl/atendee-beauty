namespace Atendee.Application.Abstractions;

public interface ICurrentActorContext
{
    Guid? ActorId { get; }
    bool IsPlatformAdministrator { get; }
}
