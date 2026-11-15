using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderAgent.Tests;

/// <summary>
/// Test authentication handler translating explicit X-Test-* headers into claims.
/// </summary>
public sealed class TestHeaderAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userId) || string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        if (Request.Headers.TryGetValue("X-Test-Tenant", out var tenant) && !string.IsNullOrWhiteSpace(tenant))
            claims.Add(new Claim("tenant_id", tenant.ToString()));

        if (Request.Headers.TryGetValue("X-Test-Scope", out var scope) && !string.IsNullOrWhiteSpace(scope))
            claims.Add(new Claim("scope", scope.ToString()));

        if (Request.Headers.TryGetValue("X-Test-Role", out var roles) && !string.IsNullOrWhiteSpace(roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
