namespace Atendee.Domain.Scheduling;

/// <summary>
/// Weekly opening intervals expressed as local wall-clock times in the tenant's
/// configured time zone. Overnight intervals must be split across two days.
/// </summary>
public sealed record BusinessHoursInterval(
    int IsoDayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed class BusinessHoursSchedule
{
    public const int MaximumIntervalsPerDay = 4;
    public const int MaximumIntervals = 28;

    private BusinessHoursSchedule(IReadOnlyList<BusinessHoursInterval> intervals)
    {
        Intervals = intervals;
    }

    public IReadOnlyList<BusinessHoursInterval> Intervals { get; }

    public static BusinessHoursSchedule Create(IEnumerable<BusinessHoursInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var supplied = intervals.ToArray();
        if (supplied.Length > MaximumIntervals)
            throw new ArgumentException("A weekly schedule cannot contain more than 28 intervals.", nameof(intervals));

        foreach (var interval in supplied)
        {
            if (interval.IsoDayOfWeek is < 1 or > 7)
                throw new ArgumentOutOfRangeException(nameof(intervals), "ISO day of week must be between 1 (Monday) and 7 (Sunday).");
            if (interval.StartTime >= interval.EndTime)
                throw new ArgumentException("Each business-hours interval must start before it ends on the same local day.", nameof(intervals));
        }

        var ordered = supplied
            .OrderBy(interval => interval.IsoDayOfWeek)
            .ThenBy(interval => interval.StartTime)
            .ToArray();

        foreach (var day in ordered.GroupBy(interval => interval.IsoDayOfWeek))
        {
            if (day.Count() > MaximumIntervalsPerDay)
                throw new ArgumentException("A day cannot contain more than four opening intervals.", nameof(intervals));

            BusinessHoursInterval? previous = null;
            foreach (var current in day)
            {
                if (previous is not null && current.StartTime < previous.EndTime)
                    throw new ArgumentException("Business-hours intervals on the same day cannot overlap.", nameof(intervals));
                previous = current;
            }
        }

        return new BusinessHoursSchedule(Array.AsReadOnly(ordered));
    }
}
