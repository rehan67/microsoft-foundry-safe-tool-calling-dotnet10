using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Infrastructure.Security;

/// <summary>
/// Evaluates intra-tenant resource ownership after coarse-grained policy and tenant scope pass.
/// </summary>
public sealed class OrderAccessAuthorizationHandler
    : AuthorizationHandler<SameTenantOrOwnerRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameTenantOrOwnerRequirement requirement,
        Order resource)
    {
        var callerTenant = context.User.FindFirstValue("tenant_id");
        var callerUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(callerTenant) || callerTenant != resource.TenantId)
            return Task.CompletedTask;

        if (context.User.IsInRole("TenantAdmin") || callerUserId == resource.CustomerUserId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
