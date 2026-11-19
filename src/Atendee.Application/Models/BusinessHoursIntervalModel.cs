namespace Atendee.Application.Models;

public sealed record BusinessHoursIntervalModel(
    Guid Id,
    int IsoDayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);
