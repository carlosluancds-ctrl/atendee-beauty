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

        // Reschedule appointment (Owner/Manager/Receptionist only)
        group.MapPost("/{id:guid}/reschedule", async (
            Guid id,
            RescheduleAppointmentRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
        {
            if (!CanWrite(current))
                return Results.Forbid();
            if (id == Guid.Empty || request is null ||
                request.NewStartAtUtc.Offset != TimeSpan.Zero ||
                request.NewStartAtUtc <= DateTimeOffset.UtcNow ||
                string.IsNullOrWhiteSpace(current.TenantTimeZoneId) ||
                current.ActorId is null ||
                !IsValidIdempotencyKey(idempotencyKey))
                return Results.BadRequest(new { error = "appointment_reschedule_validation_failed" });

            var requestHash = ComputeRequestHash(id, request.NewStartAtUtc);
            try
            {
                var rescheduled = await transactionRunner.ExecuteAsync(
                    token => appointments.RescheduleAsync(
                        id,
                        request.NewStartAtUtc,
                        idempotencyKey!,
                        requestHash,
                        current.ActorId.Value,
                        token),
                    context.RequestAborted);

                if (rescheduled is null)
                    return Results.NotFound();

                return Results.Ok(rescheduled);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
            {
                return Results.Conflict(new { error = "professional_appointment_overlap" });
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new { error = "appointment_reschedule_not_allowed" });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "appointment_reschedule_validation_failed" });
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

        // Cancel appointment (Owner/Manager/Receptionist/Professional can cancel team appointments; customers cancel via separate flow)
        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelAppointmentRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantAppointmentStore appointments) =>
        {
            if (!CanWrite(current) && !(CanReadOwn(current) && current.Role == TenantRole.Professional))
                return Results.Forbid();
            if (id == Guid.Empty || request is null)
                return Results.BadRequest(new { error = "appointment_cancel_validation_failed" });

            var isCustomerCancellation = request.IsCustomerCancellation && current.Role != TenantRole.Professional;
            try
            {
                var cancelled = await transactionRunner.ExecuteAsync(
                    token => appointments.CancelAsync(
                        id,
                        isCustomerCancellation,
                        DateTimeOffset.UtcNow,
                        token),
                    context.RequestAborted);

                if (cancelled is null)
                    return Results.NotFound();

                return Results.Ok(cancelled);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "appointment_cancel_validation_failed" });
            }
        });

        return endpoints;
    }

    // V117: Customer self-service appointment cancellation endpoints (public access via booking code)
    // Booking code format: base64(tenantId:N + appointmentId:N)
    // These endpoints do NOT require authentication; tenant is derived from the booking code
    public static IEndpointRouteBuilder MapCustomerAppointments(this IEndpointRouteBuilder endpoints)
    {
        var customerGroup = endpoints.MapGroup("/api/v1/appointments/customer");

        customerGroup.MapGet("/{bookingCode}", async (
            string bookingCode,
            IConfiguration config) =>
        {
            var decoded = DecodeBookingCode(bookingCode);
            if (decoded is null)
                return Results.BadRequest(new { error = "invalid_booking_code" });

            var (tenantId, appointmentId) = decoded.Value;

            var connectionString = config.GetConnectionString("Tenant");
            if (string.IsNullOrWhiteSpace(connectionString))
                return Results.Problem(statusCode: 500);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT appointment_id, customer_id, professional_id, service_id,
                       customer_name_snapshot, professional_name_snapshot, service_name_snapshot,
                       duration_minutes_snapshot, price_minor_units_snapshot, currency_snapshot,
                       buffer_before_minutes_snapshot, buffer_after_minutes_snapshot,
                       start_at_utc, end_at_utc, occupancy_start_at_utc, occupancy_end_at_utc,
                       status, created_at_utc, updated_at_utc
                FROM atendee.appointments
                WHERE tenant_id = @tenantId AND appointment_id = @appointmentId", conn);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("appointmentId", appointmentId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound();

            var appointment = new AppointmentModel(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                null, // Don't expose phone
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt64(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetFieldValue<DateTimeOffset>(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.GetFieldValue<DateTimeOffset>(15),
                reader.GetInt16(16).ToString(),
                reader.GetFieldValue<DateTimeOffset>(17),
                reader.GetFieldValue<DateTimeOffset>(18)
            );

            return Results.Ok(appointment);
        });

        customerGroup.MapPost("/{bookingCode}/cancel", async (
            string bookingCode,
            IConfiguration config) =>
        {
            var decoded = DecodeBookingCode(bookingCode);
            if (decoded is null)
                return Results.BadRequest(new { error = "invalid_booking_code" });

            var (tenantId, appointmentId) = decoded.Value;

            var connectionString = config.GetConnectionString("Tenant");
            if (string.IsNullOrWhiteSpace(connectionString))
                return Results.Problem(statusCode: 500);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // Check appointment exists and get current status
                await using var checkCmd = new NpgsqlCommand(@"
                    SELECT status, start_at_utc, customer_name_snapshot, professional_name_snapshot,
                           service_name_snapshot, duration_minutes_snapshot, price_minor_units_snapshot,
                           currency_snapshot, buffer_before_minutes_snapshot, buffer_after_minutes_snapshot,
                           end_at_utc, occupancy_start_at_utc, occupancy_end_at_utc,
                           customer_id, professional_id, service_id, created_at_utc, updated_at_utc
                    FROM atendee.appointments
                    WHERE tenant_id = @tenantId AND appointment_id = @appointmentId", conn, tx);
                checkCmd.Parameters.AddWithValue("tenantId", tenantId);
                checkCmd.Parameters.AddWithValue("appointmentId", appointmentId);

                await using var checkReader = await checkCmd.ExecuteReaderAsync();
                if (!await checkReader.ReadAsync())
                    return Results.NotFound();

                var status = checkReader.GetInt16(0);
                var startAtUtc = checkReader.GetFieldValue<DateTimeOffset>(1);

                if (status != (short)AppointmentStatus.Confirmed && status != (short)AppointmentStatus.PendingConfirmation)
                    return Results.Conflict(new { error = "appointment_in_invalid_status_for_cancellation" });

                // Check 24h notice for customer cancellation
                var hoursRemaining = (startAtUtc - DateTimeOffset.UtcNow).TotalHours;
                if (hoursRemaining < 24)
                    return Results.Conflict(new { error = "customer_cancellation_requires_24_hours_notice" });

                var customerName = checkReader.GetString(2);
                var professionalName = checkReader.GetString(3);
                var serviceName = checkReader.GetString(4);
                var duration = checkReader.GetInt32(5);
                var price = checkReader.GetInt64(6);
                var currency = checkReader.GetString(7);
                var bufferBefore = checkReader.GetInt32(8);
                var bufferAfter = checkReader.GetInt32(9);
                var endAtUtc = checkReader.GetFieldValue<DateTimeOffset>(10);
                var occupancyStart = checkReader.GetFieldValue<DateTimeOffset>(11);
                var occupancyEnd = checkReader.GetFieldValue<DateTimeOffset>(12);
                var customerId = checkReader.GetGuid(13);
                var professionalId = checkReader.GetGuid(14);
                var serviceId = checkReader.GetGuid(15);
                var createdAt = checkReader.GetFieldValue<DateTimeOffset>(16);
                var updatedAt = checkReader.GetFieldValue<DateTimeOffset>(17);

                await checkReader.CloseAsync();

                // Update appointment to cancelled
                await using var updateCmd = new NpgsqlCommand(@"
                    UPDATE atendee.appointments
                    SET status = @status,
                        updated_at_utc = @updatedAtUtc,
                        cancelled_at_utc = @cancelledAtUtc
                    WHERE tenant_id = @tenantId AND appointment_id = @appointmentId", conn, tx);
                updateCmd.Parameters.AddWithValue("status", (short)AppointmentStatus.Cancelled);
                updateCmd.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);
                updateCmd.Parameters.AddWithValue("cancelledAtUtc", DateTimeOffset.UtcNow);
                updateCmd.Parameters.AddWithValue("tenantId", tenantId);
                updateCmd.Parameters.AddWithValue("appointmentId", appointmentId);

                await updateCmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                // Return without phone
                return Results.Ok(new AppointmentModel(
                    appointmentId,
                    customerId,
                    professionalId,
                    serviceId,
                    customerName,
                    null, // Don't expose phone
                    professionalName,
                    serviceName,
                    duration,
                    price,
                    currency,
                    bufferBefore,
                    bufferAfter,
                    startAtUtc,
                    endAtUtc,
                    occupancyStart,
                    occupancyEnd,
                    "Cancelled",
                    createdAt,
                    DateTimeOffset.UtcNow
                ));
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });

        return endpoints;
    }

    /// <summary>
    /// Decodes a booking code into (tenantId, appointmentId).
    /// Format: base64(tenantId:N + appointmentId:N) = 32 bytes = 44 chars base64url
    /// </summary>
    private static (Guid tenantId, Guid appointmentId)? DecodeBookingCode(string bookingCode)
    {
        try
        {
            var padded = bookingCode;
            while (padded.Length % 4 != 0) padded += "=";
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 32) return null;
            return (new Guid(bytes[..16]), new Guid(bytes[16..]));
        }
        catch { return null; }
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

    private static string ComputeRequestHash(Guid appointmentId, DateTimeOffset newStartAtUtc)
    {
        var canonical = string.Join('\n',
            "appointment.reschedule.v1",
            appointmentId.ToString("N"),
            newStartAtUtc.ToString("O", CultureInfo.InvariantCulture));
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

public sealed record RescheduleAppointmentRequest(DateTimeOffset NewStartAtUtc);

public sealed record CancelAppointmentRequest(bool IsCustomerCancellation);
