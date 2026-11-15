using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderAgent.Infrastructure.Persistence;

namespace OrderAgent.Tests;

public sealed class OrderAuthorizationMatrixTests(OrderApiWebApplicationFactory factory)
    : IClassFixture<OrderApiWebApplicationFactory>
{
    private const int TargetOrderId = 1042;
    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage BuildOrderRequest(
        string path,
        string? userId = null,
        string? tenantId = null,
        string? scope = null,
        string? roles = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (userId is not null) req.Headers.Add("X-Test-User", userId);
        if (tenantId is not null) req.Headers.Add("X-Test-Tenant", tenantId);
        if (scope is not null) req.Headers.Add("X-Test-Scope", scope);
        if (roles is not null) req.Headers.Add("X-Test-Role", roles);
        return req;
    }

    [Fact]
    public async Task Case1_OwnerTenantAWithPolicy_Returns200AndStatus()
    {
        using var req = BuildOrderRequest($"/orders/{TargetOrderId}", "u_101", "tenant-a", "orders.read");
        var res = await _client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Shipped", body);
    }

    [Fact]
    public async Task Case2_OtherTenantBWithPolicy_Returns404IdenticalToNonexistent()
    {
        using var reqCrossTenant = BuildOrderRequest($"/orders/{TargetOrderId}", "u_209", "tenant-b", "orders.read");
        var resCrossTenant = await _client.SendAsync(reqCrossTenant);
        var bodyCrossTenant = await resCrossTenant.Content.ReadAsStringAsync();

        using var reqNonexistent = BuildOrderRequest("/orders/9999", "u_209", "tenant-b", "orders.read");
        var resNonexistent = await _client.SendAsync(reqNonexistent);
        var bodyNonexistent = await resNonexistent.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, resCrossTenant.StatusCode);
        Assert.Contains("order_unavailable", bodyCrossTenant);
        Assert.DoesNotContain("Shipped", bodyCrossTenant);
        Assert.Equal(bodyNonexistent, bodyCrossTenant);
    }

    [Fact]
    public async Task Case3_SignedOutCaller_Returns401Unauthorized()
    {
        var res = await _client.GetAsync($"/orders/{TargetOrderId}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Case4_MissingRoleOrScope_Returns403Forbidden()
    {
        using var req = BuildOrderRequest($"/orders/{TargetOrderId}", "u_102", "tenant-a");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task MissingTenantClaim_FailsClosedWith403Forbidden()
    {
        using var req = BuildOrderRequest($"/orders/{TargetOrderId}", "u_legacy", scope: "orders.read");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task PatternB_IntraTenantResourceHandler_AllowsOwnerAndAdminBlocksOtherCustomer()
    {
        using var reqOtherCustomer = BuildOrderRequest($"/orders/{TargetOrderId}/owner-check", "u_999", "tenant-a", "orders.read");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(reqOtherCustomer)).StatusCode);

        using var reqOwner = BuildOrderRequest($"/orders/{TargetOrderId}/owner-check", "u_101", "tenant-a", "orders.read");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(reqOwner)).StatusCode);

        using var reqAdmin = BuildOrderRequest($"/orders/{TargetOrderId}/owner-check", "u_admin", "tenant-a", roles: "TenantAdmin,OrderViewer");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(reqAdmin)).StatusCode);
    }

    [Fact]
    public async Task PatternC_GlobalQueryFilter_BlocksCrossTenantAndUnauthenticatedBackgroundReads()
    {
        using var reqCrossTenant = BuildOrderRequest($"/orders/{TargetOrderId}/filter-only", "u_209", "tenant-b", "orders.read");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(reqCrossTenant)).StatusCode);

        using var reqOwnTenant = BuildOrderRequest("/orders/2099/filter-only", "u_209", "tenant-b", "orders.read");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(reqOwnTenant)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var bgDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.Equal(0, await bgDb.Orders.CountAsync());
    }
}
