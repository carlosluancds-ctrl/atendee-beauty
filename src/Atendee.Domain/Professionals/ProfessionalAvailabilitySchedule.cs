namespace Atendee.Domain.Professionals;

/// <summary>
/// Recurring weekly availability expressed as local wall-clock intervals in the
/// tenant's configured time zone. Date-specific exceptions are modeled separately.
/// </summary>
public sealed record ProfessionalAvailabilityInterval(
    int IsoDayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed class ProfessionalAvailabilitySchedule
{
    public const int MaximumIntervalsPerDay = 4;
    public const int MaximumIntervals = 28;

    private ProfessionalAvailabilitySchedule(IReadOnlyList<ProfessionalAvailabilityInterval> intervals)
    {
        Intervals = intervals;
    }

    public IReadOnlyList<ProfessionalAvailabilityInterval> Intervals { get; }

    public static ProfessionalAvailabilitySchedule Create(
        IEnumerable<ProfessionalAvailabilityInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var supplied = intervals.ToArray();
        if (supplied.Length > MaximumIntervals)
            throw new ArgumentException("A weekly availability schedule cannot contain more than 28 intervals.", nameof(intervals));

        foreach (var interval in supplied)
        {
            if (interval.IsoDayOfWeek is < 1 or > 7)
                throw new ArgumentOutOfRangeException(nameof(intervals), "ISO day of week must be between 1 (Monday) and 7 (Sunday).");
            if (interval.StartTime >= interval.EndTime)
                throw new ArgumentException("Each availability interval must start before it ends on the same local day.", nameof(intervals));
        }

        var ordered = supplied
            .OrderBy(interval => interval.IsoDayOfWeek)
            .ThenBy(interval => interval.StartTime)
            .ToArray();

        foreach (var day in ordered.GroupBy(interval => interval.IsoDayOfWeek))
        {
            if (day.Count() > MaximumIntervalsPerDay)
                throw new ArgumentException("A day cannot contain more than four availability intervals.", nameof(intervals));

            ProfessionalAvailabilityInterval? previous = null;
            foreach (var current in day)
            {
                if (previous is not null && current.StartTime < previous.EndTime)
                    throw new ArgumentException("Availability intervals on the same day cannot overlap.", nameof(intervals));
                previous = current;
            }
        }

        return new ProfessionalAvailabilitySchedule(Array.AsReadOnly(ordered));
    }
}
