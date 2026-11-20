# Microsoft Foundry + .NET 10: Secure Agent Architecture (Auth Policies, Tenant-Scoped EF Core 10 & 4-Caller Matrix)

**Author:** Rehan Akbar  
**Role:** Software Engineer  
**Date:** 2026-11-25  
**Reading Time:** 14 min read  
**Series:** Microsoft Foundry: Zero to Production — Episode 02  
**Tags:** `Microsoft Foundry`, `.NET 10`, `ASP.NET Core`, `EF Core 10`, `Clean Architecture`, `Multi-Tenant Security`, `xUnit`, `WebApplicationFactory`

---

> **Verification & Scope Note:** All code in this walkthrough maps directly to the live `.NET 10` solution in this repository (`OrderAgentApi.slnx`) and passes `15/15` automated assertions via `dotnet test OrderAgentApi.slnx` on **ASP.NET Core `.NET 10` (SDK `10.0.404`) + `Microsoft.EntityFrameworkCore.Sqlite` `10.0.15`**.

In **Episode 01**, we traced how a Microsoft Foundry agent proposes a `get_order(orderId: 1042)` tool call and proved why the language model must never carry tenant trust inside tool arguments.

In **Episode 02**, we move from the conceptual request path into the concrete **ASP.NET Core `.NET 10` Clean Architecture backend** that enforces tenant isolation from JSON deserialization and route policies all the way down to **Entity Framework Core 10** SQL predicates and an automated **4-caller `WebApplicationFactory<Program>` verification matrix**.

---

## 1. Why `.RequireAuthorization()` Alone Fails (OWASP API1:2023 — BOLA)

A common mistake when exposing an internal API endpoint to an AI agent tool executor is assuming that adding `.RequireAuthorization()` and querying by primary key is enough:

```csharp
// ANTI-PATTERN: Authentication without tenant-scoped resource authorization
app.MapGet("/orders/{orderId:int}", async (int orderId, OrdersDbContext db) =>
{
    var order = await db.Orders.FindAsync(orderId);
    return order is not null ? Results.Ok(order) : Results.NotFound();
}).RequireAuthorization();
```

If **Tenant B** signs in with a valid JWT and asks the agent about **Order `1042`** (owned by **Tenant A**), authentication succeeds (`200 OK`) and `FindAsync(1042)` returns Tenant A's record. That is **Broken Object Level Authorization (`OWASP API1:2023`)**: authentication proves *who signed in*, never *whether that caller owns the target record*.

---

## 2. Clean Architecture Layering (`OrderAgentApi.slnx`)

To keep untrusted prompt text and tool payloads separated from persistence and security rules, `OrderAgentApi.slnx` splits responsibilities across four projects plus an integration test project:

| Project | Responsibility | Key Files |
|---|---|---|
| **`OrderAgent.Domain`** | Core entity and uniform error code (`order_unavailable`) with zero framework dependencies | `Entities/Order.cs`, `Errors/OrderErrors.cs` |
| **`OrderAgent.Application`** | Strict JSON tool contract parser (`JsonUnmappedMemberHandling.Disallow`) and use-case orchestration | `Contracts/GetOrderArguments.cs`, `Tools/GetOrderToolArgumentParser.cs`, `UseCases/GetOrderStatusHandler.cs` |
| **`OrderAgent.Infrastructure`** | Verified HTTP claim extraction (`HttpTenantContext`), EF Core 10 `OrdersDbContext` with composite indexing and fail-closed `HasQueryFilter`, and `OrderReadRepository` | `Persistence/OrdersDbContext.cs`, `Persistence/OrderReadRepository.cs`, `Security/HttpTenantContext.cs`, `Security/OrderAccessAuthorizationHandler.cs` |
| **`OrderAgent.Api`** | Named policy registration (`AuthorizationPolicies.OrderRead`), fail-closed auth handler, and Minimal API endpoints | `Authorization/AuthorizationPolicies.cs`, `Endpoints/OrderEndpoints.cs`, `Program.cs` |
| **`OrderAgent.Tests`** | In-memory `WebApplicationFactory<Program>` + SQLite 4-caller verification matrix (`15/15` passing tests) | `OrderAuthorizationMatrixTests.cs`, `ToolArgumentParserTests.cs` |

---

## 3. Hardening the Input Contract Before Routing

Before an agent tool call ever reaches an HTTP endpoint, our parser validates the raw JSON arguments from the model using `GetOrderArguments`:

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GetOrderArguments(
    [property: JsonPropertyName("orderId")] int OrderId);
```

Because `JsonUnmappedMemberHandling.Disallow` is enforced on the record, any prompt-injected payload attempting to smuggle an extra property such as `{ "orderId": 1042, "tenantId": "tenant-a" }` is rejected at deserialization before any handler runs.

---

## 4. Gate 1 — Coarse-Grained Route Policy (`AuthorizationPolicies.OrderRead`)

Gate 1 rejects anonymous callers (`401 Unauthorized`) and signed-in callers lacking order-read permission (`403 Forbidden`) before opening a database connection:

```csharp
public static class AuthorizationPolicies
{
    public const string OrderRead = "OrderRead";

    public static void AddOrderAgentPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(OrderRead, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
                HasOrderReadScope(context.User) ||
                context.User.IsInRole("OrderViewer") ||
                context.User.IsInRole("Admin"));
        });
    }
}
```

In `OrderEndpoints.cs`, attaching `[Authorize(Policy = AuthorizationPolicies.OrderRead)]` to `/orders/{orderId:int}` enforces Gate 1 declaratively inside the ASP.NET Core `.NET 10` routing middleware pipeline.

### Reading `tenant_id` Strictly from Verified Claims

Inside the endpoint, caller tenant identity is extracted exclusively from the verified `ClaimsPrincipal`—never from route parameters, query strings, headers, or model JSON. If the `tenant_id` claim is missing or blank, the endpoint fails closed immediately with `403 Forbidden`:

```csharp
var callerTenantId = user.FindFirstValue("tenant_id");
if (string.IsNullOrWhiteSpace(callerTenantId))
{
    return Results.Forbid();
}
```

---

## 5. Gate 2 — Tenant-Scoped SQL Queries & Eliminating the `403` vs `404` Enumeration Signal

Loading an order by primary key into C# memory (`FindAsync(orderId)`) and comparing `order.TenantId != callerTenantId` afterward creates two problems:
1. Foreign tenant data is materialized on your `.NET` managed heap on every unauthorized probe.
2. Returning `403 Forbidden` for Order `1042` (owned by Tenant A) versus `404 Not Found` for Order `9999` (nonexistent) gives Tenant B an **enumeration signal** revealing which competitor order numbers exist.

Instead, `OrderReadRepository.cs` pushes both predicates into SQL and projects only the safe response fields:

```csharp
public async Task<OrderStatusResponse?> GetOrderForTenantAsync(
    int orderId,
    string callerTenantId,
    CancellationToken cancellationToken = default)
{
    return await dbContext.Orders
        .AsNoTracking()
        .Where(o => o.OrderId == orderId && o.TenantId == callerTenantId)
        .Select(o => new OrderStatusResponse(o.OrderId, o.Status))
        .SingleOrDefaultAsync(cancellationToken);
}
```

When zero rows match inside Tenant B's partition, `SingleOrDefaultAsync` returns `null`, and the endpoint returns a uniform `404 Not Found` payload (`{ "code": "order_unavailable" }`)—byte-for-byte identical whether the order belongs to Tenant A or never existed.

---

## 6. Composite Indexing & Fail-Closed EF Core 10 Query Filters

In `OrdersDbContext.cs`, we pair the explicit `Where` predicate with two database-layer protections:

1. **Unique Composite Index on `(TenantId, OrderId)`** so tenant-scoped lookups seek directly into the caller's tenant partition:
   ```csharp
   entity.HasKey(o => o.OrderId);
   entity.HasIndex(o => new { o.TenantId, o.OrderId }).IsUnique();
   ```
2. **Fail-Closed Global Query Filter (`HasQueryFilter`)** bound to `_tenantContext.VerifiedTenantId`:
   ```csharp
   entity.HasQueryFilter(o => o.TenantId == _tenantContext.VerifiedTenantId);
   ```
   In `HttpTenantContext.cs`, when `HttpContext` is null (such as in a background worker) or the `tenant_id` claim is absent, `VerifiedTenantId` returns `ITenantContext.UnauthenticatedSentinel` (`"__unauthenticated__"`), guaranteeing zero rows are matched by default.

> **EF Core 10 Tip (Named Query Filters):** On `.NET 10` / `EF Core 10`, you can register multiple named query filters on the same entity (for example, `"TenantFilter"` and `"SoftDeleteFilter"`) and selectively call `.IgnoreQueryFilters(["SoftDeleteFilter"])` in audit queries without ever disabling `"TenantFilter"`.

---

## 7. Automated 4-Caller Verification Matrix (`OrderAuthorizationMatrixTests.cs`)

Never trust authorization code by visual inspection alone. In `OrderAuthorizationMatrixTests.cs`, `WebApplicationFactory<Program>` boots the real ASP.NET Core `.NET 10` pipeline in memory backed by EF Core SQLite seeded with Order `1042` (`TenantId = "tenant-a"`, `Status = "Shipped"`).

We hold `TargetOrderId = 1042` fixed and vary only caller identity across four scenarios:

| Case | Caller Identity | Scope / Role | Target | Expected HTTP Status & Body |
|---|---|---|---|---|
| **Case 1 (Owner)** | `tenant-a` (`user-a1`) | `orders.read` | `/orders/1042` | **`200 OK`** (`"status": "Shipped"`) |
| **Case 2 (Other Tenant)** | `tenant-b` (`user-b1`) | `orders.read` | `/orders/1042` | **`404 Not Found`** (`order_unavailable`, byte-identical to `/orders/9999`) |
| **Case 3 (Signed Out)** | Anonymous (no token) | None | `/orders/1042` | **`401 Unauthorized`** (blocked at Gate 1) |
| **Case 4 (Missing Scope)** | `tenant-a` (`user-a2`) | `"none"` | `/orders/1042` | **`403 Forbidden`** (blocked at Gate 1) |

Run the entire 15-test suite locally:

```bash
dotnet test OrderAgentApi.slnx --verbosity minimal
```

```text
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15
```
