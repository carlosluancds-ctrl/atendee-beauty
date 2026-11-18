using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace Atendee.Api.Tenants;

public static class TenantOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapTenantOnboarding(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenant").RequireAuthorization();

        group.MapGet("", (TrustedRequestContext current) =>
        {
            if (current.ActorId is null || current.TenantStatus is null || current.Role is null ||
                !TenantAccessPolicy.IsAllowed(current.TenantStatus.Value, current.Role.Value, TenantCapability.Login))
                return Results.Forbid();

            return Results.Ok(new TenantProfileResponse(
                current.TenantName!,
                current.TenantSlug!,
                current.TenantTimeZoneId!,
                current.TenantStatus.Value.ToString(),
                current.DisplayName!,
                current.Role.Value.ToString()));
        });

        group.MapPost("/onboarding/complete", async (
            HttpContext context,
            TrustedRequestContext current,
            UserManager<OperationalIdentityUser> users,
            IConfiguration configuration) =>
        {
            if (current.ActorId is not { } actorId ||
                current.Role != TenantRole.Owner ||
                current.TenantStatus is not { } status ||
                !TenantAccessPolicy.IsAllowed(status, TenantRole.Owner, TenantCapability.CompleteOnboarding) ||
                !context.User.FindAll("amr").Any(claim => claim.Value == "mfa"))
                return Results.Forbid();

            var user = await users.FindByIdAsync(actorId.ToString());
            if (user is null || !user.EmailConfirmed || !user.TwoFactorEnabled)
                return Results.Forbid();

            var connectionString = configuration.GetConnectionString("Tenant");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Tenant database connection is not configured.");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand(
                "SELECT tenant_id, status FROM atendee.complete_tenant_onboarding(@tenant_id, @actor_id)",
                connection);
            command.Parameters.AddWithValue("tenant_id", current.RequireTenantId().Value);
            command.Parameters.AddWithValue("actor_id", actorId);

            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted))
                return Results.Forbid();

            var tenantId = reader.GetGuid(0);
            var newStatusValue = reader.GetInt32(1);
            if (!Enum.IsDefined(typeof(TenantStatus), newStatusValue))
                throw new InvalidOperationException("Tenant onboarding returned an unsupported lifecycle status.");

            var newStatus = (TenantStatus)newStatusValue;
            return Results.Ok(new TenantOnboardingCompletionResponse(
                tenantId,
                newStatus.ToString(),
                newStatus == TenantStatus.SetupRequired));
        });

        return endpoints;
    }
}

public sealed record TenantProfileResponse(
    string Name,
    string Slug,
    string TimeZoneId,
    string Status,
    string CurrentMemberDisplayName,
    string CurrentMemberRole);

public sealed record TenantOnboardingCompletionResponse(
    Guid TenantId,
    string Status,
    bool OnboardingCompleted);
