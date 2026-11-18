namespace Atendee.Application.Models;

public sealed record ProfessionalProfileModel(
    Guid Id,
    Guid IdentityUserId,
    string DisplayName,
    bool IsActive,
    Guid[] ServiceIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
