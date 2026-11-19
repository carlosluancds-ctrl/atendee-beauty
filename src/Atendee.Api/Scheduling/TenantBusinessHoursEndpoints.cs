using System.Globalization;
using Atendee.Application.Abstractions;
using Atendee.Domain.Scheduling;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Scheduling;

public static class TenantBusinessHoursEndpoints
{
    public static IEndpointRouteBuilder MapTenantBusinessHours(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/business-hours").RequireAuthorization();

        group.MapGet("", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantBusinessHoursStore store) =>
        {
            if (!CanRead(current))
                return Results.Forbid();
            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var intervals = await transactionRunner.ExecuteAsync(
                token => store.ListAsync(token),
                context.RequestAborted);
            return Results.Ok(ToResponse(timeZoneId, intervals));
        });

        group.MapPut("", async (
            ReplaceBusinessHoursRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantBusinessHoursStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });
            if (request?.Intervals is null || request.Intervals.Count > BusinessHoursSchedule.MaximumIntervals)
                return Results.BadRequest(new { error = "business_hours_validation_failed" });

            var intervals = new List<BusinessHoursInterval>(request.Intervals.Count);
            foreach (var item in request.Intervals)
            {
                if (item is null ||
                    item.IsoDayOfWeek is < 1 or > 7 ||
                    !TimeOnly.TryParseExact(item.StartTime, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var startTime) ||
                    !TimeOnly.TryParseExact(item.EndTime, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var endTime))
                {
                    return Results.BadRequest(new { error = "business_hours_validation_failed" });
                }

                intervals.Add(new BusinessHoursInterval(item.IsoDayOfWeek, startTime, endTime));
            }

            BusinessHoursSchedule schedule;
            try
            {
                schedule = BusinessHoursSchedule.Create(intervals);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "business_hours_validation_failed" });
            }

            var saved = await transactionRunner.ExecuteAsync(
                token => store.ReplaceAsync(schedule, token),
                context.RequestAborted);
            return Results.Ok(ToResponse(timeZoneId, saved));
        });

        return endpoints;
    }

    private static BusinessHoursResponse ToResponse(
        string timeZoneId,
        IReadOnlyList<Atendee.Application.Models.BusinessHoursIntervalModel> intervals)
        => new(
            timeZoneId,
            intervals.Select(interval => new BusinessHoursIntervalResponse(
                interval.Id,
                interval.IsoDayOfWeek,
                interval.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                interval.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture))).ToArray());

    private static bool CanRead(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanManage(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.WriteData);
}

public sealed record ReplaceBusinessHoursRequest(
    IReadOnlyList<BusinessHoursIntervalRequest?>? Intervals);

public sealed record BusinessHoursIntervalRequest(
    int IsoDayOfWeek,
    string? StartTime,
    string? EndTime);

public sealed record BusinessHoursResponse(
    string TimeZoneId,
    IReadOnlyList<BusinessHoursIntervalResponse> Intervals);

public sealed record BusinessHoursIntervalResponse(
    Guid Id,
    int IsoDayOfWeek,
    string StartTime,
    string EndTime);
