namespace Atendee.Application.Models;

public sealed record SalonServiceModel(
    Guid Id,
    string Name,
    int DurationMinutes,
    long PriceMinorUnits,
    string Currency,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
