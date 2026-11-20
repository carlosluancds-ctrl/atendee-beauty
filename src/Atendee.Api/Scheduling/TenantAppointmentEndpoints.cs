using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Atendee.Domain.Scheduling;

namespace Atendee.Api.Scheduling;

public static class TenantAppointmentEndpoints
{
    public static IEndpointRouteBuilder MapTenantAppointments(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/appointments").RequireAuthorization();

        group.MapPost("", async (
            CreateAppointmentRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
        {
            if (!CanWrite(current))
                return Results.Forbid();

            if (request is null ||
                request.CustomerId == Guid.Empty ||
                request.ProfessionalId == Guid.Empty ||
                request.ServiceId == Guid.Empty ||
                request.StartAtUtc.Offset != TimeSpan.Zero ||
                request.StartAtUtc <= DateTimeOffset.UtcNow ||
                string.IsNullOrWhiteSpace(current.TenantTimeZoneId) ||
                current.ActorId is null ||
                !IsValidIdempotencyKey(idempotencyKey))
                return Results.BadRequest(new { error = "appointment_validation_failed" });

            var requestHash = ComputeRequestHash(request);
            try
            {
                var result = await transactionRunner.ExecuteAsync(
                    token => appointments.CreateAsync(
                        request.CustomerId,
                        request.ProfessionalId,
                        request.ServiceId,
                        request.StartAtUtc,
                        current.TenantTimeZoneId!,
                        idempotencyKey!,
                        requestHash,
                        current.ActorId.Value,
                        token),
                    context.RequestAborted);

                return result.Outcome switch
                {
                    AppointmentCreateOutcome.Created =>
                        Results.Created($"/api/v1/appointments/{result.Appointment!.Id}", result.Appointment),
                    AppointmentCreateOutcome.Replayed => Results.Ok(result.Appointment),
                    AppointmentCreateOutcome.IdempotencyConflict =>
                        Results.Conflict(new { error = "idempotency_key_reused_with_different_request" }),
                    AppointmentCreateOutcome.CustomerNotFound => Results.NotFound(),
                    AppointmentCreateOutcome.ServiceNotFound => Results.NotFound(),
                    AppointmentCreateOutcome.ProfessionalNotFound => Results.NotFound(),
                    AppointmentCreateOutcome.OutsideAvailability =>
                        Results.Conflict(new { error = "appointment_outside_current_availability" }),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
            {
                return Results.Conflict(new { error = "professional_appointment_overlap" });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "appointment_validation_failed" });
            }
            catch (TimeZoneNotFoundException)
            {
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });
            }
            catch (InvalidTimeZoneException)
            {
                return Results.Conflict(new { error = "tenant_time_zone_unavailable" });
            }
        });

        group.MapPost("/{id:guid}/transitions", async (
            Guid id,
            AppointmentTransitionRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments,
            ITenantProfessionalStore professionals) =>
        {
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();
            if (id == Guid.Empty || request is null ||
                !Enum.TryParse<AppointmentStatus>(request.Status, ignoreCase: false, out var nextStatus) ||
                !Enum.IsDefined(nextStatus))
                return Results.BadRequest(new { error = "appointment_transition_validation_failed" });

            if (current.Role == TenantRole.Professional)
            {
                if (nextStatus is not (AppointmentStatus.CheckedIn or AppointmentStatus.InService or AppointmentStatus.Completed) ||
                    current.ActorId is null)
                    return Results.Forbid();

                var own = await transactionRunner.ExecuteAsync(
                    token => professionals.GetOwnAsync(current.ActorId.Value, token),
                    context.RequestAborted);
                var existing = await transactionRunner.ExecuteAsync(
                    token => appointments.GetByIdAsync(id, token),
                    context.RequestAborted);
                if (own is null || !own.IsActive || existing is null ||
                    existing.ProfessionalId != own.Id)
                    return Results.NotFound();
            }
            else if (!CanWrite(current))
            {
                return Results.Forbid();
            }

            try
            {
                var updated = await transactionRunner.ExecuteAsync(
                    token => appointments.TransitionAsync(id, nextStatus, DateTimeOffset.UtcNow, token),
                    context.RequestAborted);
                if (updated is null)
                    return Results.NotFound();
                if (current.Role == TenantRole.Professional)
                    updated = updated with { CustomerPhoneSnapshot = null };
                return Results.Ok(updated);
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new { error = "appointment_transition_not_allowed" });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "appointment_was_modified_concurrently" });
            }
            catch (PostgresException exception) when (exception.SqlState == "23514")
            {
                return Results.Conflict(new { error = "appointment_transition_not_allowed" });
            }
        });
        group.MapGet("", async (
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            Guid? professionalId,
            int? page,
            int? pageSize,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments,
            ITenantProfessionalStore professionals) =>
        {
            var pageNumber = page ?? 1;
            var effectivePageSize = pageSize ?? 50;
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();
            if (fromUtc is null || toUtc is null ||
                fromUtc.Value.Offset != TimeSpan.Zero || toUtc.Value.Offset != TimeSpan.Zero ||
                fromUtc.Value >= toUtc.Value ||
                toUtc.Value - fromUtc.Value > TimeSpan.FromDays(31) ||
                professionalId == Guid.Empty || pageNumber is < 1 or > 100_000 || effectivePageSize is < 1 or > 100)
                return Results.BadRequest(new { error = "appointment_query_validation_failed" });

            Guid? effectiveProfessionalId = professionalId;
            if (current.Role == TenantRole.Professional)
            {
                if (current.ActorId is null)
                    return Results.Forbid();
                var own = await transactionRunner.ExecuteAsync(
                    token => professionals.GetOwnAsync(current.ActorId.Value, token),
                    context.RequestAborted);
                if (own is null || !own.IsActive ||
                    (professionalId.HasValue && professionalId.Value != own.Id))
                    return Results.Forbid();
                effectiveProfessionalId = own.Id;
            }

            var items = await transactionRunner.ExecuteAsync(
                token => appointments.ListAsync(fromUtc.Value, toUtc.Value, effectiveProfessionalId, pageNumber, effectivePageSize, token),
                context.RequestAborted);
            if (current.Role == TenantRole.Professional)
                items = items with
                {
                    Items = items.Items.Select(item => item with { CustomerPhoneSnapshot = null }).ToArray()
                };
            return Results.Ok(items);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments,
            ITenantProfessionalStore professionals) =>
        {
            if (!CanReadTeam(current) && !CanReadOwn(current))
                return Results.Forbid();
            if (id == Guid.Empty)
                return Results.BadRequest(new { error = "appointment_id_invalid" });

            var appointment = await transactionRunner.ExecuteAsync(
                token => appointments.GetByIdAsync(id, token),
                context.RequestAborted);
            if (appointment is null)
                return Results.NotFound();

            if (current.Role == TenantRole.Professional)
            {
                if (current.ActorId is null)
                    return Results.Forbid();
                var own = await transactionRunner.ExecuteAsync(
                    token => professionals.GetOwnAsync(current.ActorId.Value, token),
                    context.RequestAborted);
                if (own is null || own.Id != appointment.ProfessionalId)
                    return Results.NotFound();
            }

            if (current.Role == TenantRole.Professional)
                appointment = appointment with { CustomerPhoneSnapshot = null };
            return Results.Ok(appointment);
        });

        return endpoints;
    }

    private static string ComputeRequestHash(CreateAppointmentRequest request)
    {
        var canonical = string.Join('\n',
            "appointment.create.v1",
            request.CustomerId.ToString("N"),
            request.ProfessionalId.ToString("N"),
            request.ServiceId.ToString("N"),
            request.StartAtUtc.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsValidIdempotencyKey(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length is >= 16 and <= 128 &&
           value.All(character => char.IsAsciiLetterOrDigit(character) ||
                                  character is '.' or '_' or ':' or '-');

    private static bool CanReadTeam(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanReadOwn(TrustedRequestContext current)
        => current.Role == TenantRole.Professional &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, TenantRole.Professional, TenantCapability.ReadData);

    private static bool CanWrite(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.WriteData);
}

public sealed record CreateAppointmentRequest(
    Guid CustomerId,
    Guid ProfessionalId,
    Guid ServiceId,
    DateTimeOffset StartAtUtc);

public sealed record AppointmentTransitionRequest(string Status);
