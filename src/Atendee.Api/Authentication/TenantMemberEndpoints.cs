using Atendee.Api.Security;
using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace Atendee.Api.Authentication;

public static class TenantMemberEndpoints
{
    public static IEndpointRouteBuilder MapTenantMemberEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenant/members")
            .RequireAuthorization()
            .RequireRateLimiting("authenticated-api");

        // Get member info
        group.MapGet("/{identityUserId:guid}", async (
            Guid identityUserId,
            HttpContext context,
            TrustedRequestContext current,
            ITenantMembershipResolver membershipResolver) =>
        {
            if (identityUserId == Guid.Empty)
                return Results.BadRequest(new { error = "identity_user_id_invalid" });

            var requesterMembership = await membershipResolver.ResolveAsync(current.ActorId!.Value);
            if (requesterMembership is null || !requesterMembership.IsActive)
                return Results.Forbid();

            var targetMembership = await membershipResolver.ResolveAsync(identityUserId);
            if (targetMembership is null)
                return Results.NotFound();

            if (requesterMembership.TenantId != targetMembership.TenantId)
                return Results.NotFound();

            return Results.Ok(new
            {
                targetMembership.IdentityUserId,
                targetMembership.DisplayName,
                Role = targetMembership.Role.ToString(),
                targetMembership.IsActive
            });
        });

        // List members (Owner and Manager only)
        group.MapGet("", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantMembershipResolver membershipResolver) =>
        {
            var requesterMembership = await membershipResolver.ResolveAsync(current.ActorId!.Value);
            if (requesterMembership is null || !requesterMembership.IsActive)
                return Results.Forbid();

            if (requesterMembership.Role is not TenantRole.Owner and not TenantRole.Manager)
                return Results.Forbid();

            // This would need a proper query - for now return 501
            return Results.Problem(
                detail: "Listing members requires a dedicated query endpoint. Use individual member lookup.",
                statusCode: 501);
        });

        // Change member role (Owner only; Manager for Receptionist/Professional)
        group.MapPut("/{identityUserId:guid}/role", async (
            Guid identityUserId,
            ChangeRoleRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IOperationalAccountSecurityService security,
            ITenantMembershipResolver membershipResolver) =>
        {
            if (identityUserId == Guid.Empty || request is null || !Enum.IsDefined(request.Role))
                return Results.BadRequest(new { error = "role_change_validation_failed" });

            var requesterMembership = await membershipResolver.ResolveAsync(current.ActorId!.Value);
            if (requesterMembership is null || !requesterMembership.IsActive)
                return Results.Forbid();

            var targetMembership = await membershipResolver.ResolveAsync(identityUserId);
            if (targetMembership is null || !targetMembership.IsActive)
                return Results.NotFound();

            if (requesterMembership.TenantId != targetMembership.TenantId)
                return Results.NotFound();

            // Authorization check
            if (requesterMembership.Role == TenantRole.Owner)
            {
                if (current.ActorId == identityUserId)
                    return Results.BadRequest(new { error = "cannot_change_own_role" });
                if (targetMembership.Role == TenantRole.Owner)
                    return Results.Forbid();
            }
            else if (requesterMembership.Role == TenantRole.Manager)
            {
                if (targetMembership.Role == TenantRole.Owner || targetMembership.Role == TenantRole.Manager)
                    return Results.Forbid();
                if (request.Role == TenantRole.Owner || request.Role == TenantRole.Manager)
                    return Results.Forbid();
            }
            else
            {
                return Results.Forbid();
            }

            try
            {
                var changed = await security.ChangeRoleAsync(
                    current.ActorId!.Value,
                    identityUserId,
                    request.Role);

                return changed ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Deactivate member (Owner; Manager for Receptionist/Professional)
        group.MapPost("/{identityUserId:guid}/deactivate", async (
            Guid identityUserId,
            DeactivateMemberRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IOperationalAccountSecurityService security,
            ITenantMembershipResolver membershipResolver) =>
        {
            if (identityUserId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "deactivate_validation_failed" });

            var requesterMembership = await membershipResolver.ResolveAsync(current.ActorId!.Value);
            if (requesterMembership is null || !requesterMembership.IsActive)
                return Results.Forbid();

            var targetMembership = await membershipResolver.ResolveAsync(identityUserId);
            if (targetMembership is null || !targetMembership.IsActive)
                return Results.NotFound();

            if (requesterMembership.TenantId != targetMembership.TenantId)
                return Results.NotFound();

            if (current.ActorId == identityUserId)
                return Results.BadRequest(new { error = "cannot_deactivate_self" });

            if (targetMembership.Role == TenantRole.Owner)
                return Results.Forbid();

            if (requesterMembership.Role == TenantRole.Manager &&
                targetMembership.Role != TenantRole.Receptionist &&
                targetMembership.Role != TenantRole.Professional)
                return Results.Forbid();

            try
            {
                var deactivated = await security.DeactivateMembershipAsync(
                    current.ActorId!.Value,
                    identityUserId);

                return deactivated ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Reactivate member (Owner; Manager for Receptionist/Professional)
        group.MapPost("/{identityUserId:guid}/reactivate", async (
            Guid identityUserId,
            ReactivateMemberRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            IOperationalAccountSecurityService security,
            ITenantMembershipResolver membershipResolver) =>
        {
            if (identityUserId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reactivate_validation_failed" });

            var requesterMembership = await membershipResolver.ResolveAsync(current.ActorId!.Value);
            if (requesterMembership is null || !requesterMembership.IsActive)
                return Results.Forbid();

            var targetMembership = await membershipResolver.ResolveAsync(identityUserId);
            if (targetMembership is null)
                return Results.NotFound();

            if (requesterMembership.TenantId != targetMembership.TenantId)
                return Results.NotFound();

            if (current.ActorId == identityUserId)
                return Results.BadRequest(new { error = "cannot_reactivate_self" });

            if (requesterMembership.Role == TenantRole.Manager &&
                targetMembership.Role != TenantRole.Receptionist &&
                targetMembership.Role != TenantRole.Professional)
                return Results.Forbid();

            try
            {
                var reactivated = await security.ReactivateMembershipAsync(
                    current.ActorId!.Value,
                    identityUserId);

                return reactivated ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        return endpoints;
    }
}

public sealed record ChangeRoleRequest(TenantRole Role);
public sealed record DeactivateMemberRequest(string Reason);
public sealed record ReactivateMemberRequest(string Reason);