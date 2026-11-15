using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OrderAgent.Api.Authorization;

/// <summary>
/// Fail-closed default authentication scheme for Episode 01 local sample runs.
/// Replace with AddJwtBearer(...) wired to Microsoft Entra ID or your OIDC provider in Episode 02.
/// </summary>
public sealed class FailClosedAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
