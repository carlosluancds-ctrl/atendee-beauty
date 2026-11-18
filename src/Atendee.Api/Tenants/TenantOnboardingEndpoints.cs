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
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);

            // atendee_app_login has no direct table or schema privileges. Enter the
            // restricted tenant runtime role only for this transaction.
            await using (var roleCommand = new NpgsqlCommand(
                "SET LOCAL ROLE atendee_runtime", connection, transaction))
            {
                await roleCommand.ExecuteNonQueryAsync(context.RequestAborted);
            }

            Guid tenantId;
            int newStatusValue;
            await using (var command = new NpgsqlCommand(
                "SELECT tenant_id, status FROM atendee.complete_tenant_onboarding(@tenant_id, @actor_id)",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("tenant_id", current.RequireTenantId().Value);
                command.Parameters.AddWithValue("actor_id", actorId);

                await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Forbid();
                }

                tenantId = reader.GetGuid(0);
                newStatusValue = reader.GetInt32(1);
            }

            if (!Enum.IsDefined(typeof(TenantStatus), newStatusValue))
                throw new InvalidOperationException("Tenant onboarding returned an unsupported lifecycle status.");

            await transaction.CommitAsync(context.RequestAborted);

            var newStatus = (TenantStatus)newStatusValue;
            return Results.Ok(new TenantOnboardingCompletionResponse(
                tenantId,
                newStatus.ToString(),
                newStatus == TenantStatus.SetupRequired));
        });

        return endpoints;
    }
}

public sealed record TenantOnboardingCompletionResponse(
    Guid TenantId,
    string Status,
    bool OnboardingCompleted);
