namespace Atendee.Application.Models;

public sealed record ProfessionalTimeOffModel(
    Guid Id,
    Guid ProfessionalId,
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime);
