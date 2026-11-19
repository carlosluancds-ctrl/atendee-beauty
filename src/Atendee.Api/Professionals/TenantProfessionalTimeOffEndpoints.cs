using System.Globalization;
using Atendee.Application.Abstractions;
using Atendee.Domain.Professionals;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Professionals;

public static class TenantProfessionalTimeOffEndpoints
{
    public static IEndpointRouteBuilder MapTenantProfessionalTimeOff(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/professionals").RequireAuthorization();

        group.MapGet("/{professionalId:guid}/time-off", async (
            Guid professionalId,
            string? from,
            string? to,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalTimeOffStore timeOff) =>
        {
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();
            if (professionalId == Guid.Empty ||
                !DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fromDate) ||
                !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var toDate) ||
                fromDate > toDate ||
                toDate.DayNumber - fromDate.DayNumber > 365)
                return Results.BadRequest(new { error = "professional_time_off_validation_failed" });

            if (current.Role == TenantRole.Professional && current.ActorId is null)
                return Results.Forbid();
            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var response = await transactionRunner.ExecuteAsync(async token =>
            {
                var profile = current.Role == TenantRole.Professional
                    ? await professionals.GetOwnAsync(current.ActorId!.Value, token)
                    : (await professionals.ListAsync(token)).SingleOrDefault(item => item.Id == professionalId);

                if (profile is null || !profile.IsActive || profile.Id != professionalId)
                    return null;

                var entries = await timeOff.ListAsync(professionalId, fromDate, toDate, token);
                return entries.Select(entry => ToResponse(entry, timeZoneId)).ToArray();
            }, context.RequestAborted);

            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        group.MapPost("/{professionalId:guid}/time-off", async (
            Guid professionalId,
            CreateProfessionalTimeOffRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalTimeOffStore timeOff) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });
            if (professionalId == Guid.Empty ||
                !DateOnly.TryParseExact(request?.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) ||
                !TryParseOptionalTime(request?.StartTime, out var start) ||
                !TryParseOptionalTime(request?.EndTime, out var end) ||
                start.HasValue != end.HasValue ||
                (start.HasValue && start.Value >= end!.Value))
                return Results.BadRequest(new { error = "professional_time_off_validation_failed" });

            try
            {
                var response = await transactionRunner.ExecuteAsync(async token =>
                {
                    var profile = (await professionals.ListAsync(token))
                        .SingleOrDefault(item => item.Id == professionalId);
                    if (profile is null || !profile.IsActive)
                        return null;

                    return await timeOff.CreateAsync(
                        professionalId, new ProfessionalTimeOffInterval(date, start, end), token);
                }, context.RequestAborted);

                return response is null
                    ? Results.NotFound()
                    : Results.Created($"/api/v1/professionals/{professionalId}/time-off?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}",
                        ToResponse(response, timeZoneId));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "professional_time_off_validation_failed" });
            }
        });

        group.MapPost("/{professionalId:guid}/time-off/{timeOffId:guid}/archive", async (
            Guid professionalId,
            Guid timeOffId,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore professionals,
            ITenantProfessionalTimeOffStore timeOff) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (professionalId == Guid.Empty || timeOffId == Guid.Empty)
                return Results.BadRequest(new { error = "professional_time_off_validation_failed" });
            var timeZoneId = current.TenantTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });

            var response = await transactionRunner.ExecuteAsync(async token =>
            {
                var profile = (await professionals.ListAsync(token))
                    .SingleOrDefault(item => item.Id == professionalId);
                if (profile is null || !profile.IsActive)
                    return null;
                return await timeOff.ArchiveAsync(professionalId, timeOffId, token);
            }, context.RequestAborted);

            return response is null ? Results.NotFound() : Results.Ok(ToResponse(response, timeZoneId));
        });

        return endpoints;
    }

    private static bool TryParseOptionalTime(string? value, out TimeOnly? time)
    {
        time = null;
        if (value is null)
            return true;
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        time = parsed;
        return true;
    }

    private static ProfessionalTimeOffResponse ToResponse(
        Atendee.Application.Models.ProfessionalTimeOffModel model,
        string timeZoneId)
        => new(model.Id, model.ProfessionalId, timeZoneId,
            model.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            model.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            model.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture));

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

public sealed record CreateProfessionalTimeOffRequest(string? Date, string? StartTime, string? EndTime);

public sealed record ProfessionalTimeOffResponse(
    Guid Id,
    Guid ProfessionalId,
    string TimeZoneId,
    string Date,
    string? StartTime,
    string? EndTime);
