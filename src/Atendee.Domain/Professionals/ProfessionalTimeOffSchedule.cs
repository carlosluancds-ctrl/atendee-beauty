namespace Atendee.Domain.Professionals;

/// <summary>
/// Date-specific time off in the tenant's local calendar and wall-clock time.
/// A null start/end pair represents a full local day; partial intervals subtract
/// from recurring availability and never extend it.
/// </summary>
public sealed record ProfessionalTimeOffInterval(
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime);

public sealed class ProfessionalTimeOffSchedule
{
    public const int MaximumIntervalsPerDate = 4;

    private ProfessionalTimeOffSchedule(IReadOnlyList<ProfessionalTimeOffInterval> intervals)
        => Intervals = intervals;

    public IReadOnlyList<ProfessionalTimeOffInterval> Intervals { get; }

    public static ProfessionalTimeOffSchedule Create(IEnumerable<ProfessionalTimeOffInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var supplied = intervals.ToArray();

        foreach (var interval in supplied)
        {
            if (interval is null)
                throw new ArgumentException("Time-off entries cannot contain null values.", nameof(intervals));
            if (interval.StartTime.HasValue != interval.EndTime.HasValue)
                throw new ArgumentException("A time-off interval must specify both local times or neither.", nameof(intervals));
            if (interval.StartTime is { } start && interval.EndTime is { } end && start >= end)
                throw new ArgumentException("A partial-day time-off interval must start before it ends on the same local day.", nameof(intervals));
        }

        var ordered = supplied
            .OrderBy(interval => interval.Date)
            .ThenBy(interval => interval.StartTime ?? TimeOnly.MinValue)
            .ToArray();

        foreach (var day in ordered.GroupBy(interval => interval.Date))
        {
            if (day.Count() > MaximumIntervalsPerDate)
                throw new ArgumentException("A date cannot contain more than four active time-off intervals.", nameof(intervals));

            var fullDay = day.Any(interval => interval.StartTime is null);
            if (fullDay && day.Count() > 1)
                throw new ArgumentException("A full-day time-off entry cannot coexist with another entry on the same date.", nameof(intervals));

            ProfessionalTimeOffInterval? previous = null;
            foreach (var current in day)
            {
                if (previous?.EndTime is { } previousEnd &&
                    current.StartTime is { } currentStart &&
                    currentStart < previousEnd)
                    throw new ArgumentException("Time-off intervals on the same date cannot overlap.", nameof(intervals));
                previous = current;
            }
        }

        return new ProfessionalTimeOffSchedule(Array.AsReadOnly(ordered));
    }
}
