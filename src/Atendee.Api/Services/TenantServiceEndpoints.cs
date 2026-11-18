using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Web;

namespace Atendee.Api.Services;

public static class TenantServiceEndpoints
{
    public static IEndpointRouteBuilder MapTenantServices(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/services").RequireAuthorization();

        group.MapGet("", async (
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantServiceStore store) =>
        {
            if (!CanRead(current))
                return Results.Forbid();

            var services = await transactionRunner.ExecuteAsync(
                token => store.ListAsync(token), context.RequestAborted);
            return Results.Ok(services);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantServiceStore store) =>
        {
            if (!CanRead(current))
                return Results.Forbid();

            var service = await transactionRunner.ExecuteAsync(
                token => store.GetByIdAsync(id, token), context.RequestAborted);
            return service is null ? Results.NotFound() : Results.Ok(service);
        });

        group.MapPost("", async (
            UpsertSalonServiceRequest request,
            HttpContext context,
            TrustedRequestContext current,
            ILogger<TenantServiceEndpoints> logger,
            ITenantTransactionRunner transactionRunner,
            ITenantServiceStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (!request.IsComplete())
            {
                logger.LogWarning(
                    "Service request rejected as incomplete. NamePresent={NamePresent}, DurationPresent={DurationPresent}, PricePresent={PricePresent}, CurrencyPresent={CurrencyPresent}, BufferBeforePresent={BufferBeforePresent}, BufferAfterPresent={BufferAfterPresent}, IsActivePresent={IsActivePresent}",
                    !string.IsNullOrWhiteSpace(request.Name),
                    request.DurationMinutes.HasValue,
                    request.PriceMinorUnits.HasValue,
                    !string.IsNullOrWhiteSpace(request.Currency),
                    request.BufferBeforeMinutes.HasValue,
                    request.BufferAfterMinutes.HasValue,
                    request.IsActive.HasValue);
                return Results.BadRequest(new { error = "service_validation_failed" });
            }

            try
            {
                var service = await transactionRunner.ExecuteAsync(
                    token => store.CreateAsync(
                        request.Name!, request.DurationMinutes!.Value,
                        request.PriceMinorUnits!.Value, request.Currency!,
                        request.BufferBeforeMinutes!.Value, request.BufferAfterMinutes!.Value,
                        request.IsActive!.Value, token),
                    context.RequestAborted);
                return Results.Created($"/api/v1/services/{service.Id}", service);
            }
            catch (ArgumentException exception)
            {
                logger.LogWarning(
                    "Service domain validation rejected request. Parameter={ParameterName}",
                    exception.ParamName);
                return Results.BadRequest(new { error = "service_validation_failed" });
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpsertSalonServiceRequest request,
            HttpContext context,
            TrustedRequestContext current,
            ITenantTransactionRunner transactionRunner,
            ITenantServiceStore store) =>
        {
            if (!CanManage(current))
                return Results.Forbid();
            if (id == Guid.Empty || !request.IsComplete())
                return Results.BadRequest(new { error = "service_validation_failed" });

            try
            {
                var service = await transactionRunner.ExecuteAsync(
                    token => store.UpdateAsync(
                        id, request.Name!, request.DurationMinutes!.Value,
                        request.PriceMinorUnits!.Value, request.Currency!,
                        request.BufferBeforeMinutes!.Value, request.BufferAfterMinutes!.Value,
                        request.IsActive!.Value, token),
                    context.RequestAborted);
                return service is null ? Results.NotFound() : Results.Ok(service);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "service_validation_failed" });
            }
        });

        return endpoints;
    }

    private static bool CanRead(TrustedRequestContext current)
        => current.Role is TenantRole.Owner or TenantRole.Manager or TenantRole.Receptionist &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.ReadData);

    private static bool CanManage(TrustedRequestContext current)
        => current.Role is TenantRole.Owner or TenantRole.Manager &&
           current.TenantStatus is { } status &&
           TenantAccessPolicy.IsAllowed(status, current.Role.Value, TenantCapability.WriteData);
}

public sealed record UpsertSalonServiceRequest(
    string? Name,
    int? DurationMinutes,
    long? PriceMinorUnits,
    string? Currency,
    int? BufferBeforeMinutes,
    int? BufferAfterMinutes,
    bool? IsActive)
{
    public bool IsComplete()
        => !string.IsNullOrWhiteSpace(Name) &&
           DurationMinutes.HasValue &&
           PriceMinorUnits.HasValue &&
           !string.IsNullOrWhiteSpace(Currency) &&
           BufferBeforeMinutes.HasValue &&
           BufferAfterMinutes.HasValue &&
           IsActive.HasValue;
}
