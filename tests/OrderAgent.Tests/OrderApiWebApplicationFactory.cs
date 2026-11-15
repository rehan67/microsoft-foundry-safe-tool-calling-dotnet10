using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace OrderAgent.Tests;

/// <summary>
/// Boots OrderAgent.Api in-memory via TestServer with header-based test authentication.
/// </summary>
public sealed class OrderApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestHeader";
                options.DefaultChallengeScheme = "TestHeader";
                options.DefaultForbidScheme = "TestHeader";
            })
            .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthHandler>("TestHeader", _ => { });
        });
    }
}
