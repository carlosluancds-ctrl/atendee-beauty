using Atendee.Api.Security;
using Atendee.Infrastructure.Security;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace Atendee.Api.PlatformAdmin;

public static class PlatformAdminEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAdmin(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform-admin")
            .RequireAuthorization()
            .RequireRateLimiting("platform-admin");

        // List Platform Admins
        group.MapGet("/admins", async (
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            var admins = await platformAdmin.ListPlatformAdminsAsync();
            return Results.Ok(admins);
        });

        // Create Platform Admin
        group.MapPost("/admins", async (
            CreatePlatformAdminRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (request is null ||
                request.IdentityUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.DisplayName) ||
                !IsValidIdempotencyKey(idempotencyKey))
                return Results.BadRequest(new { error = "platform_admin_create_validation_failed" });

            try
            {
                var admin = await platformAdmin.CreatePlatformAdminAsync(
                    request.IdentityUserId,
                    request.Email,
                    request.DisplayName,
                    current.ActorId!.Value,
                    "Platform Admin creation via API");
                return Results.Created($"/api/v1/platform-admin/admins/{admin.IdentityUserId}", admin);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Remove Platform Admin
        group.MapDelete("/admins/{identityUserId:guid}", async (
            Guid identityUserId,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (identityUserId == Guid.Empty)
                return Results.BadRequest(new { error = "identity_user_id_invalid" });

            try
            {
                var removed = await platformAdmin.RemovePlatformAdminAsync(
                    identityUserId,
                    current.ActorId!.Value,
                    "Platform Admin removal via API");
                return removed ? Results.NoContent() : Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // List all tenants (cross-tenant)
        group.MapGet("/tenants", async (
            int? page,
            int? pageSize,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            var pageNumber = page ?? 1;
            var effectivePageSize = pageSize ?? 50;
            var tenants = await platformAdmin.ListAllTenantsAsync(pageNumber, effectivePageSize);
            return Results.Ok(tenants);
        });

        // Get tenant by ID
        group.MapGet("/tenants/{tenantId:guid}", async (
            Guid tenantId,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (tenantId == Guid.Empty)
                return Results.BadRequest(new { error = "tenant_id_invalid" });

            var tenant = await platformAdmin.GetTenantAsync(tenantId);
            return tenant is not null ? Results.Ok(tenant) : Results.NotFound();
        });

        // Suspend tenant
        group.MapPost("/tenants/{tenantId:guid}/suspend", async (
            Guid tenantId,
            SuspendTenantRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (tenantId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "tenant_suspend_validation_failed" });

            try
            {
                var suspended = await platformAdmin.SuspendTenantAsync(
                    tenantId,
                    current.ActorId!.Value,
                    request.Reason);
                return suspended ? Results.NoContent() : Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Unsuspend tenant
        group.MapPost("/tenants/{tenantId:guid}/unsuspend", async (
            Guid tenantId,
            UnsuspendTenantRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (tenantId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "tenant_unsuspend_validation_failed" });

            try
            {
                var unsuspended = await platformAdmin.UnsuspendTenantAsync(
                    tenantId,
                    current.ActorId!.Value,
                    request.Reason);
                return unsuspended ? Results.NoContent() : Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Delete tenant
        group.MapDelete("/tenants/{tenantId:guid}", async (
            Guid tenantId,
            DeleteTenantRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (tenantId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "tenant_delete_validation_failed" });

            try
            {
                var deleted = await platformAdmin.DeleteTenantAsync(
                    tenantId,
                    current.ActorId!.Value,
                    request.Reason);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Four-eyes: request authorization
        group.MapPost("/four-eyes/request", async (
            FourEyesRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (request is null ||
                string.IsNullOrWhiteSpace(request.Action) ||
                string.IsNullOrWhiteSpace(request.ResourceType) ||
                string.IsNullOrWhiteSpace(request.ResourceId) ||
                string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "four_eyes_request_validation_failed" });

            var fourEyes = await platformAdmin.RequestFourEyesAsync(
                current.ActorId!.Value,
                request.Action,
                request.ResourceType,
                request.ResourceId,
                request.Reason);
            return Results.Ok(fourEyes);
        });

        // Four-eyes: approve
        group.MapPost("/four-eyes/{requestId:guid}/approve", async (
            Guid requestId,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (requestId == Guid.Empty)
                return Results.BadRequest(new { error = "request_id_invalid" });

            try
            {
                var approved = await platformAdmin.ApproveFourEyesAsync(requestId, current.ActorId!.Value);
                return approved ? Results.NoContent() : Results.Conflict(new { error = "four_eyes_approve_failed" });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Four-eyes: reject
        group.MapPost("/four-eyes/{requestId:guid}/reject", async (
            Guid requestId,
            FourEyesRejectRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (requestId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "four_eyes_reject_validation_failed" });

            try
            {
                var rejected = await platformAdmin.RejectFourEyesAsync(
                    requestId,
                    current.ActorId!.Value,
                    request.Reason);
                return rejected ? Results.NoContent() : Results.Conflict(new { error = "four_eyes_reject_failed" });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Query audit log
        group.MapGet("/audit", async (
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            string? action,
            Guid? actorIdentityUserId,
            int? page,
            int? pageSize,
            HttpContext context,
            TrustedRequestContext current,
            IPlatformAdminService platformAdmin) =>
        {
            if (!await platformAdmin.IsPlatformAdminAsync(current.ActorId!.Value))
                return Results.Forbid();

            if (fromUtc == default || toUtc == default || fromUtc > toUtc ||
                toUtc - fromUtc > TimeSpan.FromDays(366))
                return Results.BadRequest(new { error = "audit_query_validation_failed" });

            var pageNumber = page ?? 1;
            var effectivePageSize = pageSize ?? 50;
            var auditPage = await platformAdmin.QueryAuditLogAsync(
                fromUtc, toUtc, action, actorIdentityUserId, pageNumber, effectivePageSize);
            return Results.Ok(auditPage);
        });

        return endpoints;
    }

    private static bool IsValidIdempotencyKey(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length is >= 16 and <= 128 &&
           value.All(character => char.IsAsciiLetterOrDigit(character) ||
                                  character is '.' or '_' or ':' or '-');
}

public sealed record CreatePlatformAdminRequest(
    Guid IdentityUserId,
    string Email,
    string DisplayName);

public sealed record SuspendTenantRequest(string Reason);

public sealed record UnsuspendTenantRequest(string Reason);

public sealed record DeleteTenantRequest(string Reason);

public sealed record AddTenantAdminRequest(
    Guid IdentityUserId,
    string Role);

public sealed record FourEyesRequest(
    string Action,
    string ResourceType,
    string ResourceId,
    string Reason);

public sealed record FourEyesRejectRequest(string Reason);