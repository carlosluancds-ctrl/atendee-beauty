using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Professionals;

public static class TenantProfessionalEndpoints
{
    public static IEndpointRouteBuilder MapTenantProfessionals(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/professionals").RequireAuthorization();

        group.MapGet("", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore store) =>
        {
            if (!CanReadTeam(current))
                return Results.Forbid();
            var profiles = await transactionRunner.ExecuteAsync(
                token => store.ListAsync(token), context.RequestAborted);
            return Results.Ok(profiles);
        });

        group.MapGet("/me", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore store) =>
        {
            if (current.Role != TenantRole.Professional ||
                current.ActorId is not { } actorId ||
                current.TenantStatus is not { } status ||
                !TenantAccessPolicy.IsAllowed(status, TenantRole.Professional, TenantCapability.ReadData))
                return Results.Forbid();

            var profile = await transactionRunner.ExecuteAsync(
                token => store.GetOwnAsync(actorId, token), context.RequestAborted);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPost("", async (
            CreateProfessionalRequest request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (request.IdentityUserId == Guid.Empty)
                return Results.BadRequest(new { error = "professional_validation_failed" });

            try
            {
                var profile = await transactionRunner.ExecuteAsync(
                    token => store.CreateAsync(request.IdentityUserId, token),
                    context.RequestAborted);
                return Results.Created($"/api/v1/professionals/{profile.Id}", profile);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "professional_membership_invalid" });
            }
        });

        group.MapPut("/{id:guid}/services", async (
            Guid id,
            SetProfessionalServicesRequest request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantProfessionalStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (id == Guid.Empty || request.ServiceIds is null ||
                request.ServiceIds.Length > 100 ||
                request.ServiceIds.Any(serviceId => serviceId == Guid.Empty) ||
                request.ServiceIds.Distinct().Count() != request.ServiceIds.Length)
                return Results.BadRequest(new { error = "professional_services_validation_failed" });

            try
            {
                var profile = await transactionRunner.ExecuteAsync(
                    token => store.SetServicesAsync(id, request.ServiceIds, token),
                    context.RequestAborted);
                return profile is null ? Results.NotFound() : Results.Ok(profile);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "professional_services_validation_failed" });
            }
        });

        return endpoints;
    }

    private static bool CanReadTeam(TrustedRequestContext current)
        => current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanManage(TrustedRequestContext current)
        => current.Role is TenantRole.Owner or TenantRole.Manager &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ManageTeamAndSettings);
}

public sealed record CreateProfessionalRequest(Guid IdentityUserId);
public sealed record SetProfessionalServicesRequest(Guid[]? ServiceIds);
