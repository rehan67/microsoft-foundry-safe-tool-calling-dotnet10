using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderAgent.Application.Abstractions;

namespace OrderAgent.Infrastructure.Security;

/// <summary>
/// Resolves the caller's verified tenant claim from the active HTTP request or fails closed.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public string VerifiedTenantId =>
        accessor.HttpContext?.User.FindFirstValue("tenant_id") is { Length: > 0 } tenant
            ? tenant
            : ITenantContext.UnauthenticatedSentinel;
}
