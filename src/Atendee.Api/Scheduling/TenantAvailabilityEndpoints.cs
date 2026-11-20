using System.Globalization;
using Atendee.Application.Abstractions;
using Atendee.Domain.Catalog;
using Atendee.Domain.Professionals;
using Atendee.Domain.Scheduling;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Scheduling;

public static class TenantAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapTenantAvailability(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/availability").RequireAuthorization();

        group.MapGet("", async (
            string? from,
            string? to,
            Guid? serviceId,
            Guid? professionalId,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantBusinessHoursStore businessHours,
            ITenantProfessionalStore professionals,
            ITenantProfessionalAvailabilityStore availability,
            ITenantProfessionalTimeOffStore timeOff,
            ITenantServiceStore services,
            ITenantAppointmentStore appointments) =>
        {
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();

            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fromDate) ||
                !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var toDate) ||
                fromDate > toDate ||
                toDate.DayNumber - fromDate.DayNumber >= ServiceAvailabilityCalculator.MaximumDateRangeDays ||
                toDate == DateOnly.MaxValue ||
                serviceId is null || serviceId == Guid.Empty ||
                professionalId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "availability_query_validation_failed" });
            }

            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            if (current.Role == TenantRole.Professional && current.ActorId is null)
                return Results.Forbid();

            var inputs = await transactionRunner.ExecuteAsync(async token =>
            {
                var serviceModel = await services.GetByIdAsync(serviceId.Value, token);
                if (serviceModel is null || !serviceModel.IsActive)
                    return AvailabilityQueryResult.NotFound;

                var service = new SalonService(
                    serviceModel.Id, serviceModel.Name, serviceModel.DurationMinutes,
                    serviceModel.PriceMinorUnits, serviceModel.Currency,
                    serviceModel.BufferBeforeMinutes, serviceModel.BufferAfterMinutes,
                    serviceModel.IsActive, serviceModel.CreatedAtUtc, serviceModel.UpdatedAtUtc);

                var openingIntervals = (await businessHours.ListAsync(token))
                    .Select(interval => new BusinessHoursInterval(
                        interval.IsoDayOfWeek, interval.StartTime, interval.EndTime))
                    .ToArray();

                IReadOnlyList<Atendee.Application.Models.ProfessionalProfileModel> profiles;
                if (current.Role == TenantRole.Professional)
                {
                    var own = await professionals.GetOwnAsync(current.ActorId!.Value, token);
                    if (own is null || !own.IsActive ||
                        (professionalId.HasValue && professionalId.Value != own.Id))
                        return AvailabilityQueryResult.Forbidden;
                    profiles = [own];
                }
                else
                {
                    var team = await professionals.ListAsync(token);
                    profiles = professionalId.HasValue
                        ? team.Where(profile => profile.Id == professionalId.Value).ToArray()
                        : team;
                }

                if (professionalId.HasValue &&
                    !profiles.Any(profile => profile.Id == professionalId.Value && profile.IsActive))
                    return AvailabilityQueryResult.NotFound;

                var profileInputs = new List<AvailabilityProfileInputs>();
                foreach (var profile in profiles.Where(profile =>
                             profile.IsActive && profile.ServiceIds.Contains(serviceId.Value)))
                {
                    var recurring = (await availability.ListAsync(profile.Id, token))
                        .Select(interval => new ProfessionalAvailabilityInterval(
                            interval.IsoDayOfWeek, interval.StartTime, interval.EndTime))
                        .ToArray();
                    var exceptions = (await timeOff.ListAsync(profile.Id, fromDate, toDate, token))
                        .Select(interval => new ProfessionalTimeOffInterval(
                            interval.Date, interval.StartTime, interval.EndTime))
                        .ToArray();
                    var occupancyFromUtc = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(-14);
                    var occupancyToUtc = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(14);
                    var occupied = await appointments.ListBlockingOccupancyAsync(
                        profile.Id, occupancyFromUtc, occupancyToUtc, token);
                    profileInputs.Add(new AvailabilityProfileInputs(profile.Id, recurring, exceptions, occupied));
                }

                return AvailabilityQueryResult.Success(service, openingIntervals, profileInputs);
            }, context.RequestAborted);

            if (inputs.Kind == AvailabilityQueryResultKind.Forbidden)
                return Results.Forbid();
            if (inputs.Kind == AvailabilityQueryResultKind.NotFound)
                return Results.NotFound();

            var serviceDefinition = inputs.Service!;
            var results = inputs.Profiles.Select(profile =>
                new ProfessionalServiceAvailabilityResponse(
                    profile.ProfessionalId,
                    ServiceAvailabilityCalculator.SubtractOccupiedWindows(
                    ServiceAvailabilityCalculator.Calculate(
                        fromDate, toDate, timeZoneId,
                        inputs.BusinessHours,
                        profile.RecurringAvailability,
                        profile.TimeOff,
                        serviceDefinition),
                    profile.OccupiedIntervals,
                    serviceDefinition)
                    .Select(window => new ServiceAvailabilityWindowResponse(
                        window.EarliestStartUtc,
                        window.LatestStartUtc))
                    .ToArray())).ToArray();

            return Results.Ok(new ServiceAvailabilityResponse(
                timeZoneId,
                fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                serviceId.Value,
                results));
        });

        return endpoints;
    }

    private static bool CanReadTeam(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanReadOwn(TrustedRequestContext current)
        => current.Role == TenantRole.Professional &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, TenantRole.Professional, TenantCapability.ReadData);
}

internal sealed record AvailabilityProfileInputs(
    Guid ProfessionalId,
    IReadOnlyList<ProfessionalAvailabilityInterval> RecurringAvailability,
    IReadOnlyList<ProfessionalTimeOffInterval> TimeOff,
    IReadOnlyList<ProfessionalOccupancyInterval> OccupiedIntervals);

internal enum AvailabilityQueryResultKind
{
    Success,
    Forbidden,
    NotFound
}

internal sealed record AvailabilityQueryResult(
    AvailabilityQueryResultKind Kind,
    SalonService? Service,
    IReadOnlyList<BusinessHoursInterval> BusinessHours,
    IReadOnlyList<AvailabilityProfileInputs> Profiles)
{
    public static AvailabilityQueryResult Forbidden { get; } =
        new(AvailabilityQueryResultKind.Forbidden, null, [], []);

    public static AvailabilityQueryResult NotFound { get; } =
        new(AvailabilityQueryResultKind.NotFound, null, [], []);

    public static AvailabilityQueryResult Success(
        SalonService service,
        IReadOnlyList<BusinessHoursInterval> businessHours,
        IReadOnlyList<AvailabilityProfileInputs> profiles)
        => new(AvailabilityQueryResultKind.Success, service, businessHours, profiles);
}

public sealed record ServiceAvailabilityResponse(
    string TimeZoneId,
    string From,
    string To,
    Guid ServiceId,
    IReadOnlyList<ProfessionalServiceAvailabilityResponse> Professionals);

public sealed record ProfessionalServiceAvailabilityResponse(
    Guid ProfessionalId,
    IReadOnlyList<ServiceAvailabilityWindowResponse> Windows);

public sealed record ServiceAvailabilityWindowResponse(
    DateTimeOffset EarliestStartUtc,
    DateTimeOffset LatestStartUtc);