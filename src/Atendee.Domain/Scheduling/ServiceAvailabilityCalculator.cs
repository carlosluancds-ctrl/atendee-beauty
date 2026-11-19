using Atendee.Domain.Catalog;
using Atendee.Domain.Professionals;

namespace Atendee.Domain.Scheduling;

/// <summary>
/// A continuous UTC window in which a service may start. LatestStartUtc is inclusive
/// at one-minute precision; the represented working interval itself is half-open.
/// </summary>
public sealed record ServiceAvailabilityWindow(
    DateTimeOffset EarliestStartUtc,
    DateTimeOffset LatestStartUtc);

/// <summary>
/// Calculates safe service-start windows from tenant-local business hours,
/// professional availability and date-specific time off.
/// UTC timeline traversal naturally skips nonexistent DST wall times and
/// represents both occurrences of ambiguous wall times without guessing an offset.
/// </summary>
public static class ServiceAvailabilityCalculator
{
    public const int MaximumDateRangeDays = 31;

    public static IReadOnlyList<ServiceAvailabilityWindow> Calculate(
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        IReadOnlyList<BusinessHoursInterval> businessHours,
        IReadOnlyList<ProfessionalAvailabilityInterval> professionalAvailability,
        IReadOnlyList<ProfessionalTimeOffInterval> timeOff,
        SalonService service)
    {
        ArgumentNullException.ThrowIfNull(businessHours);
        ArgumentNullException.ThrowIfNull(professionalAvailability);
        ArgumentNullException.ThrowIfNull(timeOff);
        ArgumentNullException.ThrowIfNull(service);

        if (from > to || to.DayNumber - from.DayNumber >= MaximumDateRangeDays)
            throw new ArgumentOutOfRangeException(nameof(to), "Availability range must be between one and 31 inclusive calendar days.");
        if (to == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(to));
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (!service.IsActive)
            throw new ArgumentException("Only active services can be scheduled.", nameof(service));

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("Tenant time zone is not available on this host.", nameof(timeZoneId), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Tenant time zone data is invalid.", nameof(timeZoneId), exception);
        }

        var business = BusinessHoursSchedule.Create(businessHours).Intervals;
        var professional = ProfessionalAvailabilitySchedule.Create(professionalAvailability).Intervals;
        var exceptions = ProfessionalTimeOffSchedule.Create(timeOff).Intervals;

        if (business.Count == 0 || professional.Count == 0)
            return [];

        // Time-zone offsets are bounded by +/-14 hours. Scan the UTC timeline
        // with a margin so every instant belonging to the requested local dates
        // is considered, including both sides of a DST transition.
        var scanStart = new DateTimeOffset(
            from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(-14);
        var scanEnd = new DateTimeOffset(
            to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(14);

        var windows = new List<ServiceAvailabilityWindow>();
        DateTimeOffset? openStart = null;

        for (var instant = scanStart; instant < scanEnd; instant = instant.AddMinutes(1))
        {
            var local = TimeZoneInfo.ConvertTime(instant, timeZone);
            var localDate = DateOnly.FromDateTime(local.DateTime);
            var available = localDate >= from && localDate <= to &&
                IsOpen(business, localDate, TimeOnly.FromDateTime(local.DateTime)) &&
                IsOpen(professional, localDate, TimeOnly.FromDateTime(local.DateTime)) &&
                !IsBlocked(exceptions, localDate, TimeOnly.FromDateTime(local.DateTime));

            if (available)
            {
                openStart ??= instant;
            }
            else if (openStart is { } start)
            {
                AddWindow(windows, start, instant, service);
                openStart = null;
            }
        }

        if (openStart is { } finalStart)
            AddWindow(windows, finalStart, scanEnd, service);

        return windows.AsReadOnly();
    }

    private static bool IsOpen(
        IReadOnlyList<BusinessHoursInterval> intervals,
        DateOnly date,
        TimeOnly time)
    {
        var isoDay = (int)date.DayOfWeek;
        isoDay = isoDay == 0 ? 7 : isoDay;
        return intervals.Any(interval =>
            interval.IsoDayOfWeek == isoDay &&
            time >= interval.StartTime &&
            time < interval.EndTime);
    }

    private static bool IsOpen(
        IReadOnlyList<ProfessionalAvailabilityInterval> intervals,
        DateOnly date,
        TimeOnly time)
    {
        var isoDay = (int)date.DayOfWeek;
        isoDay = isoDay == 0 ? 7 : isoDay;
        return intervals.Any(interval =>
            interval.IsoDayOfWeek == isoDay &&
            time >= interval.StartTime &&
            time < interval.EndTime);
    }

    private static bool IsBlocked(
        IReadOnlyList<ProfessionalTimeOffInterval> intervals,
        DateOnly date,
        TimeOnly time)
        => intervals.Any(interval =>
            interval.Date == date &&
            (interval.StartTime is null ||
             (time >= interval.StartTime.Value && time < interval.EndTime!.Value)));

    private static void AddWindow(
        ICollection<ServiceAvailabilityWindow> windows,
        DateTimeOffset openStartUtc,
        DateTimeOffset openEndUtcExclusive,
        SalonService service)
    {
        var earliest = openStartUtc.AddMinutes(service.BufferBeforeMinutes);
        var latest = openEndUtcExclusive
            .AddMinutes(-service.DurationMinutes - service.BufferAfterMinutes);

        if (earliest <= latest)
            windows.Add(new ServiceAvailabilityWindow(earliest, latest));
    }
}