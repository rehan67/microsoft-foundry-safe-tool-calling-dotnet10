using Microsoft.AspNetCore.Authorization;

namespace OrderAgent.Infrastructure.Security;

/// <summary>
/// Authorization requirement verifying same-tenant ownership or TenantAdmin privilege.
/// </summary>
public sealed class SameTenantOrOwnerRequirement : IAuthorizationRequirement;
