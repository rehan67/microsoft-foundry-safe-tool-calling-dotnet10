using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OrderAgent.Api.Authorization;
using OrderAgent.Application.Contracts;
using OrderAgent.Application.UseCases;
using OrderAgent.Domain.Errors;
using OrderAgent.Infrastructure.Security;

namespace OrderAgent.Api.Endpoints;

/// <summary>
/// Minimal API route mappings enforcing Gate 1 policy checks and Gate 2 tenant isolation.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Pattern A: Policy gate + tenant-scoped SQL query
        endpoints.MapGet("/orders/{orderId:int}", [Authorize(Policy = AuthorizationPolicies.OrderRead)] async (
            int orderId,
            ClaimsPrincipal user,
            GetOrderStatusHandler handler,
            CancellationToken ct) =>
        {
            var callerTenantId = user.FindFirstValue("tenant_id");
            if (string.IsNullOrWhiteSpace(callerTenantId))
                return Results.Forbid();

            var order = await handler.HandleAsync(orderId, callerTenantId, ct);
            return order is null
                ? Results.NotFound(new { code = OrderErrors.UnavailableCode })
                : Results.Ok(order);
        });

        // Pattern B: Tenant-scoped query + imperative IAuthorizationService ownership check
        endpoints.MapGet("/orders/{orderId:int}/owner-check", [Authorize(Policy = AuthorizationPolicies.OrderRead)] async (
            int orderId,
            ClaimsPrincipal user,
            GetOrderStatusHandler handler,
            IAuthorizationService authService,
            CancellationToken ct) =>
        {
            var callerTenantId = user.FindFirstValue("tenant_id");
            if (string.IsNullOrWhiteSpace(callerTenantId))
                return Results.Forbid();

            var entity = await handler.GetEntityForAuthorizationAsync(orderId, callerTenantId, ct);
            if (entity is null)
                return Results.NotFound(new { code = OrderErrors.UnavailableCode });

            var authResult = await authService.AuthorizeAsync(user, entity, new SameTenantOrOwnerRequirement());
            if (!authResult.Succeeded)
                return Results.NotFound(new { code = OrderErrors.UnavailableCode });

            return Results.Ok(new OrderStatusResponse(entity.OrderId, entity.Status));
        });

        // Pattern C safety-net demo: omits explicit TenantId filter to prove EF Global Query Filter blocks leak
        endpoints.MapGet("/orders/{orderId:int}/filter-only", [Authorize(Policy = AuthorizationPolicies.OrderRead)] async (
            int orderId,
            GetOrderStatusHandler handler,
            CancellationToken ct) =>
        {
            var order = await handler.HandleWithGlobalFilterOnlyAsync(orderId, ct);
            return order is null
                ? Results.NotFound(new { code = OrderErrors.UnavailableCode })
                : Results.Ok(order);
        });

        return endpoints;
    }
}
