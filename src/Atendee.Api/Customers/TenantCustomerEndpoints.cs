using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Customers;

public static class TenantCustomerEndpoints
{
    public static IEndpointRouteBuilder MapTenantCustomers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").RequireAuthorization();

        group.MapGet("", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantCustomerStore store,
            int page = 1,
            int pageSize = 50) =>
        {
            if (!CanRead(current))
                return Results.Forbid();
            if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
                return Results.BadRequest(new { error = "pagination_invalid" });

            var customers = await transactionRunner.ExecuteAsync(
                token => store.ListAsync(page, pageSize, token),
                context.RequestAborted);
            return Results.Ok(customers);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantCustomerStore store) =>
        {
            if (!CanRead(current))
                return Results.Forbid();

            var customer = await transactionRunner.ExecuteAsync(
                token => store.GetByIdAsync(id, token),
                context.RequestAborted);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        });

        group.MapPost("", async (
            UpsertCustomerRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantCustomerStore store) =>
        {
            if (!CanWrite(current))
                return Results.Forbid();
            if (request is null || !request.IsComplete())
                return Results.BadRequest(new { error = "customer_validation_failed" });

            try
            {
                var customer = await transactionRunner.ExecuteAsync(
                    token => store.CreateAsync(
                        request.FullName!,
                        request.PhoneNumber,
                        request.EmailAddress,
                        token),
                    context.RequestAborted);
                return Results.Created($"/api/v1/customers/{customer.Id}", customer);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "customer_validation_failed" });
            }
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpsertCustomerRequest? request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantCustomerStore store) =>
        {
            if (!CanWrite(current))
                return Results.Forbid();
            if (id == Guid.Empty || request is null || !request.IsComplete())
                return Results.BadRequest(new { error = "customer_validation_failed" });

            try
            {
                var customer = await transactionRunner.ExecuteAsync(
                    token => store.UpdateAsync(
                        id,
                        request.FullName!,
                        request.PhoneNumber,
                        request.EmailAddress,
                        token),
                    context.RequestAborted);
                return customer is null ? Results.NotFound() : Results.Ok(customer);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "customer_validation_failed" });
            }
        });

        group.MapPost("/{id:guid}/archive", async (
            Guid id,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantCustomerStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (id == Guid.Empty)
                return Results.BadRequest(new { error = "customer_validation_failed" });

            var customer = await transactionRunner.ExecuteAsync(
                token => store.ArchiveAsync(id, token),
                context.RequestAborted);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        });

        return endpoints;
    }

    private static bool CanRead(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanWrite(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.WriteData);

    private static bool CanManage(TrustedRequestContext current)
        => (current.Role is TenantRole.Owner or TenantRole.Manager) &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.WriteData);
}

public sealed record UpsertCustomerRequest(
    string? FullName,
    string? PhoneNumber,
    string? EmailAddress)
{
    public bool IsComplete()
        => !string.IsNullOrWhiteSpace(FullName) &&
           (!string.IsNullOrWhiteSpace(PhoneNumber) || !string.IsNullOrWhiteSpace(EmailAddress));
}
