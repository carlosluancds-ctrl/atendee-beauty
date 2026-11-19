namespace Atendee.Application.Models;

public sealed record ProfessionalAvailabilityIntervalModel(
    Guid Id,
    Guid ProfessionalId,
    int IsoDayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);
