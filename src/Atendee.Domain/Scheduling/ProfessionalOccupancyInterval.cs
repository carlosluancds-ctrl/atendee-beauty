namespace Atendee.Domain.Scheduling;

/// <summary>Half-open UTC occupancy interval for a professional-blocking appointment.</summary>
public sealed record ProfessionalOccupancyInterval(DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc)
{
    public static ProfessionalOccupancyInterval Create(DateTimeOffset startAtUtc, DateTimeOffset endAtUtc)
    {
        if (startAtUtc.Offset != TimeSpan.Zero || endAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Occupancy boundaries must be UTC.");
        if (startAtUtc >= endAtUtc)
            throw new ArgumentException("Occupancy interval must have positive duration.");
        return new ProfessionalOccupancyInterval(startAtUtc, endAtUtc);
    }
}