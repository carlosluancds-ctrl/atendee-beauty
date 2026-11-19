using System.Globalization;
using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Professionals;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Professionals;

public static class TenantProfessionalAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapTenantProfessionalAvailability(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/professionals").RequireAuthorization();

        group.MapGet("/me/availability", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalAvailabilityStore availability) =>
        {
            if (!CanReadOwn(current) || current.ActorId is not { } actorId)
                return Results.Forbid();

            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var response = await transactionRunner.ExecuteAsync(async token =>
            {
                var profile = await professionals.GetOwnAsync(actorId, token);
                if (profile is null || !profile.IsActive)
                    return null;

                var intervals = await availability.ListAsync(profile.Id, token);
                return ToResponse(profile.Id, timeZoneId, intervals);
            }, context.RequestAborted);

            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        group.MapGet("/{professionalId:guid}/availability", async (
            Guid professionalId,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalAvailabilityStore availability) =>
        {
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();
            if (professionalId == Guid.Empty)
                return Results.BadRequest(new { error = "professional_availability_validation_failed" });
            if (current.Role == TenantRole.Professional && current.ActorId is null)
                return Results.Forbid();

            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var response = await transactionRunner.ExecuteAsync(async token =>
            {
                ProfessionalProfileModel? profile;
                if (current.Role == TenantRole.Professional)
                {
                    profile = await professionals.GetOwnAsync(current.ActorId!.Value, token);
                    if (profile?.Id != professionalId)
                        return null;
                }
                else
                {
                    profile = (await professionals.ListAsync(token))
                        .SingleOrDefault(item => item.Id == professionalId);
                }

                if (profile is null || !profile.IsActive)
                    return null;

                var intervals = await availability.ListAsync(profile.Id, token);
                return ToResponse(profile.Id, timeZoneId, intervals);
            }, context.RequestAborted);

            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        group.MapPut("/{professionalId:guid}/availability", async (
            Guid professionalId,
            ReplaceProfessionalAvailabilityRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalAvailabilityStore availability) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (professionalId == Guid.Empty ||
                request?.Intervals is null ||
                request.Intervals.Count > ProfessionalAvailabilitySchedule.MaximumIntervals)
            {
                return Results.BadRequest(new { error = "professional_availability_validation_failed" });
            }

            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var intervals = new List<ProfessionalAvailabilityInterval>(request.Intervals.Count);
            foreach (var item in request.Intervals)
            {
                if (item is null ||
                    item.IsoDayOfWeek is < 1 or > 7 ||
                    !TimeOnly.TryParseExact(item.StartTime, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var startTime) ||
                    !TimeOnly.TryParseExact(item.EndTime, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var endTime))
                {
                    return Results.BadRequest(new { error = "professional_availability_validation_failed" });
                }

                intervals.Add(new ProfessionalAvailabilityInterval(item.IsoDayOfWeek, startTime, endTime));
            }

            ProfessionalAvailabilitySchedule schedule;
            try
            {
                schedule = ProfessionalAvailabilitySchedule.Create(intervals);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "professional_availability_validation_failed" });
            }

            var response = await transactionRunner.ExecuteAsync(async token =>
            {
                var profile = (await professionals.ListAsync(token))
                    .SingleOrDefault(item => item.Id == professionalId);
                if (profile is null || !profile.IsActive)
                    return null;

                var saved = await availability.ReplaceAsync(professionalId, schedule, token);
                return ToResponse(professionalId, timeZoneId, saved);
            }, context.RequestAborted);

            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        return endpoints;
    }

    private static ProfessionalAvailabilityResponse ToResponse(
        Guid professionalId,
        string timeZoneId,
        IReadOnlyList<ProfessionalAvailabilityIntervalModel> intervals)
        => new(
            professionalId,
            timeZoneId,
            intervals.Select(interval => new ProfessionalAvailabilityIntervalResponse(
                interval.Id,
                interval.IsoDayOfWeek,
                interval.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                interval.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture))).ToArray());

    private static bool CanReadTeam(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanReadOwn(TrustedRequestContext current)
        => current.Role == TenantRole.Professional &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, TenantRole.Professional, TenantCapability.ReadData);

    private static bool CanManage(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ManageTeamAndSettings);
}

public sealed record ReplaceProfessionalAvailabilityRequest(
    IReadOnlyList<ProfessionalAvailabilityIntervalRequest?>? Intervals);

public sealed record ProfessionalAvailabilityIntervalRequest(
    int IsoDayOfWeek,
    string? StartTime,
    string? EndTime);

public sealed record ProfessionalAvailabilityResponse(
    Guid ProfessionalId,
    string TimeZoneId,
    IReadOnlyList<ProfessionalAvailabilityIntervalResponse> Intervals);

public sealed record ProfessionalAvailabilityIntervalResponse(
    Guid Id,
    int IsoDayOfWeek,
    string StartTime,
    string EndTime);
