using Microsoft.AspNetCore.Authentication;
using OrderAgent.Api.Authorization;
using OrderAgent.Api.Endpoints;
using OrderAgent.Infrastructure;
using OrderAgent.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrderAgentInfrastructure();

// Episode 01 ships with a fail-closed default auth scheme; Episode 02 wires real JwtBearer OIDC auth.
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, FailClosedAuthHandler>("Bearer", _ => { });

builder.Services.AddOrderAgentAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await OrderSeeder.SeedSampleOrdersAsync(db);
}

app.UseAuthentication();
app.UseAuthorization();
app.MapOrderEndpoints();

app.Run();

public partial class Program;
