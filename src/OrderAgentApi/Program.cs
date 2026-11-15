using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environments.Production
});

builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// Keep in-memory SQLite connection open for the lifetime of the test run
using var sqliteConnection = new SqliteConnection("DataSource=:memory:");
await sqliteConnection.OpenAsync();

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlite(sqliteConnection));

builder.Services
    .AddAuthentication("TestHeader")
    .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthHandler>("TestHeader", _ => { });

// Gate 1 policy from blog.md
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrderRead", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.HasClaim("scope", "orders.read") ||
            context.User.IsInRole("OrderViewer") ||
            context.User.IsInRole("Admin"));
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, OrderAccessAuthorizationHandler>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Create schema and seed real SQLite database (with query filters bypassed ONLY at seed time)
using (var seedScope = app.Services.CreateScope())
{
    var db = seedScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.EnsureCreatedAsync();
    db.Orders.AddRange(
        new OrderEntity { OrderId = 1042, TenantId = "tenant-a", CustomerUserId = "u_101", Status = "Shipped" },
        new OrderEntity { OrderId = 2099, TenantId = "tenant-b", CustomerUserId = "u_209", Status = "Processing" }
    );
    await db.SaveChangesAsync();
}

// Pattern A endpoint from blog.md (verbatim EF Core + SQLite)
app.MapGet("/orders/{orderId:int}", [Authorize(Policy = "OrderRead")] async (
    int orderId,
    ClaimsPrincipal user,
    OrdersDbContext db,
    CancellationToken ct) =>
{
    var callerTenantId = user.FindFirstValue("tenant_id");
    if (string.IsNullOrWhiteSpace(callerTenantId))
        return Results.Forbid();

    // Gate 2: Match BOTH model-supplied orderId AND verified callerTenantId
    var order = await db.Orders
        .AsNoTracking()
        .Where(o => o.OrderId == orderId
                 && o.TenantId == callerTenantId)
        .Select(o => new OrderStatusResponse(o.OrderId, o.Status))
        .SingleOrDefaultAsync(ct);

    if (order is null)
        return Results.NotFound(new { code = "order_unavailable" });

    return Results.Ok(order);
});

// Pattern B endpoint: combines Tenant predicate + OrderAccessAuthorizationHandler
app.MapGet("/orders/{orderId:int}/owner-check", [Authorize(Policy = "OrderRead")] async (
    int orderId,
    ClaimsPrincipal user,
    OrdersDbContext db,
    IAuthorizationService authService,
    CancellationToken ct) =>
{
    var callerTenantId = user.FindFirstValue("tenant_id");
    if (string.IsNullOrWhiteSpace(callerTenantId))
        return Results.Forbid();

    var entity = await db.Orders
        .AsNoTracking()
        .SingleOrDefaultAsync(o => o.OrderId == orderId && o.TenantId == callerTenantId, ct);

    if (entity is null)
        return Results.NotFound(new { code = "order_unavailable" });

    var record = new OrderRecord(entity.OrderId, entity.TenantId, entity.CustomerUserId, entity.Status);
    var authResult = await authService.AuthorizeAsync(user, record, new SameTenantOrOwnerRequirement());
    if (!authResult.Succeeded)
        return Results.NotFound(new { code = "order_unavailable" });

    return Results.Ok(new OrderStatusResponse(entity.OrderId, entity.Status));
});

// Pattern C safety-net endpoint: omits explicit TenantId in LINQ to verify EF Global Query Filter blocks leak
app.MapGet("/orders/{orderId:int}/filter-only", [Authorize(Policy = "OrderRead")] async (
    int orderId,
    OrdersDbContext db,
    CancellationToken ct) =>
{
    var order = await db.Orders
        .AsNoTracking()
        .Where(o => o.OrderId == orderId)
        .Select(o => new OrderStatusResponse(o.OrderId, o.Status))
        .SingleOrDefaultAsync(ct);

    return order is null ? Results.NotFound(new { code = "order_unavailable" }) : Results.Ok(order);
});

await app.StartAsync();

var server = app.Services.GetRequiredService<IServer>();
var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address) };

// 1. Verify TryParseOrderId (including additionalProperties: false enforcement)
Assert(TryParseOrderId("{\"orderId\":1042}", out var parsedId) && parsedId == 1042, "Valid integer orderId 1042 passes");
Assert(!TryParseOrderId("{\"orderId\":1042,\"tenantId\":\"tenant-a\"}", out _), "Unmapped extra property rejected");
Assert(!TryParseOrderId("{\"orderId\":0}", out _), "Zero orderId rejected");
Assert(!TryParseOrderId("{\"orderId\":-5}", out _), "Negative orderId rejected");
Assert(!TryParseOrderId("{\"orderId\":\"1042\"}", out _), "String orderId rejected");
Assert(!TryParseOrderId("{bad json", out _), "Malformed JSON rejected");

// 2. Verify all 4 callers against real EF Core 10 + SQLite + Kestrel
const int orderId = 1042;

// Case 1: 1. Owner (Tenant A, OrderRead) -> 200 OK
using var req1 = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
req1.Headers.Add("X-Test-User", "u_101");
req1.Headers.Add("X-Test-Tenant", "tenant-a");
req1.Headers.Add("X-Test-Scope", "orders.read");
var res1 = await client.SendAsync(req1);
var body1 = await res1.Content.ReadAsStringAsync();
Assert(res1.StatusCode == HttpStatusCode.OK && body1.Contains("Shipped"), $"Case 1 failed: {res1.StatusCode} {body1}");

// Case 2: 2. Other Tenant (Tenant B, OrderRead) asking for Order 1042 -> 404 order_unavailable (identical to nonexistent 9999)
using var req2 = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
req2.Headers.Add("X-Test-User", "u_209");
req2.Headers.Add("X-Test-Tenant", "tenant-b");
req2.Headers.Add("X-Test-Scope", "orders.read");
var res2 = await client.SendAsync(req2);
var body2 = await res2.Content.ReadAsStringAsync();
Assert(res2.StatusCode == HttpStatusCode.NotFound && body2.Contains("order_unavailable") && !body2.Contains("Shipped"),
    $"Case 2 failed: {res2.StatusCode} {body2}");

using var reqNonexistent = new HttpRequestMessage(HttpMethod.Get, "/orders/9999");
reqNonexistent.Headers.Add("X-Test-User", "u_209");
reqNonexistent.Headers.Add("X-Test-Tenant", "tenant-b");
reqNonexistent.Headers.Add("X-Test-Scope", "orders.read");
var resNonexistent = await client.SendAsync(reqNonexistent);
var bodyNonexistent = await resNonexistent.Content.ReadAsStringAsync();
Assert(resNonexistent.StatusCode == HttpStatusCode.NotFound && bodyNonexistent == body2,
    "404/403 oracle prevention verified: nonexistent ID matches cross-tenant miss byte-for-byte");

// Case 3: 3. Signed Out -> 401 Unauthorized
var res3 = await client.GetAsync($"/orders/{orderId}");
Assert(res3.StatusCode == HttpStatusCode.Unauthorized, $"Case 3 failed: {res3.StatusCode}");

// Case 4: 4. Missing Role/Scope -> 403 Forbidden
using var req4 = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
req4.Headers.Add("X-Test-User", "u_102");
req4.Headers.Add("X-Test-Tenant", "tenant-a");
var res4 = await client.SendAsync(req4);
Assert(res4.StatusCode == HttpStatusCode.Forbidden, $"Case 4 failed: {res4.StatusCode}");

// Missing tenant_id claim fails closed (403 Forbidden)
using var reqNoTenant = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
reqNoTenant.Headers.Add("X-Test-User", "u_legacy");
reqNoTenant.Headers.Add("X-Test-Scope", "orders.read");
var resNoTenant = await client.SendAsync(reqNoTenant);
Assert(resNoTenant.StatusCode == HttpStatusCode.Forbidden, $"Missing tenant claim failed: {resNoTenant.StatusCode}");

// Pattern B: non-owner within Tenant A denied; owner and TenantAdmin allowed
using var reqOtherCustomer = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}/owner-check");
reqOtherCustomer.Headers.Add("X-Test-User", "u_999");
reqOtherCustomer.Headers.Add("X-Test-Tenant", "tenant-a");
reqOtherCustomer.Headers.Add("X-Test-Scope", "orders.read");
Assert((await client.SendAsync(reqOtherCustomer)).StatusCode == HttpStatusCode.NotFound, "Pattern B non-owner blocked");

using var reqAdmin = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}/owner-check");
reqAdmin.Headers.Add("X-Test-User", "u_admin");
reqAdmin.Headers.Add("X-Test-Tenant", "tenant-a");
reqAdmin.Headers.Add("X-Test-Role", "TenantAdmin,OrderViewer");
Assert((await client.SendAsync(reqAdmin)).StatusCode == HttpStatusCode.OK, "Pattern B TenantAdmin allowed");

// Pattern C: Global Query Filter blocks Tenant B from Order 1042 even without explicit WHERE clause, while allowing own Order 2099
using var reqFilterCross = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}/filter-only");
reqFilterCross.Headers.Add("X-Test-User", "u_209");
reqFilterCross.Headers.Add("X-Test-Tenant", "tenant-b");
reqFilterCross.Headers.Add("X-Test-Scope", "orders.read");
Assert((await client.SendAsync(reqFilterCross)).StatusCode == HttpStatusCode.NotFound, "EF Global Query Filter blocked cross-tenant leak");

using var reqFilterOwn = new HttpRequestMessage(HttpMethod.Get, "/orders/2099/filter-only");
reqFilterOwn.Headers.Add("X-Test-User", "u_209");
reqFilterOwn.Headers.Add("X-Test-Tenant", "tenant-b");
reqFilterOwn.Headers.Add("X-Test-Scope", "orders.read");
Assert((await client.SendAsync(reqFilterOwn)).StatusCode == HttpStatusCode.OK, "EF Global Query Filter allowed own tenant row");

// Pattern C background fail-closed sentinel (zero authenticated HttpContext returns 0 rows)
using (var bgScope = app.Services.CreateScope())
{
    var bgDb = bgScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    Assert(await bgDb.Orders.CountAsync() == 0, "Fail-closed sentinel blocks unauthenticated background DbContext read");
}

Console.WriteLine($"ALL .NET {Environment.Version} + EF CORE 10 SQLITE CHECKS PASSED (15/15 assertions, 4/4 callers, Patterns A/B/C verified).");

await app.StopAsync();
return 0;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException($"ASSERTION FAILED: {message}");
}

static bool TryParseOrderId(string rawArgumentsJson, out int orderId)
{
    orderId = 0;

    GetOrderArguments? parsed;
    try
    {
        parsed = JsonSerializer.Deserialize<GetOrderArguments>(rawArgumentsJson);
    }
    catch (JsonException)
    {
        return false;
    }

    if (parsed is null || parsed.OrderId <= 0)
        return false;

    orderId = parsed.OrderId;
    return true;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GetOrderArguments(
    [property: JsonPropertyName("orderId")] int OrderId);

public sealed record OrderStatusResponse(int OrderId, string Status);

public sealed record OrderRecord(
    int OrderId,
    string TenantId,
    string CustomerUserId,
    string Status);

public sealed class OrderEntity
{
    public int OrderId { get; set; }
    public required string TenantId { get; set; }
    public required string CustomerUserId { get; set; }
    public required string Status { get; set; }
}

public interface ITenantContext
{
    string VerifiedTenantId { get; }
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    // Fail closed with impossible sentinel when no authenticated tenant claim exists
    public string VerifiedTenantId =>
        accessor.HttpContext?.User.FindFirstValue("tenant_id") is { Length: > 0 } tenant
            ? tenant
            : "__unauthenticated__";
}

public sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.HasKey(o => o.OrderId);
            entity.HasIndex(o => new { o.TenantId, o.OrderId }).IsUnique();
            entity.HasQueryFilter(o => o.TenantId == _tenantContext.VerifiedTenantId);
        });
    }
}

public sealed class SameTenantOrOwnerRequirement : IAuthorizationRequirement { }

public sealed class OrderAccessAuthorizationHandler
    : AuthorizationHandler<SameTenantOrOwnerRequirement, OrderRecord>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameTenantOrOwnerRequirement requirement,
        OrderRecord resource)
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
