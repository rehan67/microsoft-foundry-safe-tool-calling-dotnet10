# How Microsoft Foundry Agents Call Tools Safely (.NET 10 — Clean Architecture)

Companion source code and engineering walkthrough for **Episode 01** of **Microsoft Foundry: Zero to Production** by **Rehan Akbar**.

Demonstrates why authentication alone is insufficient when an AI agent proposes tool calls (`get_order(orderId)`), and how to enforce **multi-tenant authorization** inside an **ASP.NET Core (.NET 10)** backend structured with **Clean Architecture** using a **Two-Gate Authorization Model** before querying data.

---

## Clean Architecture Layout

```text
.
├── .gitignore
├── README.md
├── OrderAgentApi.slnx
├── docs/
│   └── blog.md                                          # Complete Episode 01 technical walkthrough
├── src/
│   ├── OrderAgent.Domain/                               # Zero external dependencies
│   │   ├── Entities/Order.cs
│   │   └── Errors/OrderErrors.cs
│   ├── OrderAgent.Application/                          # Depends only on Domain
│   │   ├── Abstractions/{IOrderReadRepository,ITenantContext}.cs
│   │   ├── Contracts/{GetOrderArguments,OrderStatusResponse}.cs
│   │   ├── Tools/GetOrderToolArgumentParser.cs
│   │   └── UseCases/GetOrderStatusHandler.cs
│   ├── OrderAgent.Infrastructure/                       # EF Core SQLite + ASP.NET Core Resource Handler
│   │   ├── DependencyInjection.cs
│   │   ├── Persistence/{OrdersDbContext,OrderReadRepository,OrderSeeder}.cs
│   │   └── Security/{HttpTenantContext,OrderAccessAuthorizationHandler,SameTenantOrOwnerRequirement}.cs
│   └── OrderAgent.Api/                                  # Thin Minimal API composition root
│       ├── Authorization/{AuthorizationPolicies,FailClosedAuthHandler}.cs
│       ├── Endpoints/OrderEndpoints.cs
│       └── Program.cs
└── tests/
    └── OrderAgent.Tests/                                # xUnit + WebApplicationFactory<Program>
        ├── OrderApiWebApplicationFactory.cs
        ├── OrderAuthorizationMatrixTests.cs
        ├── TestHeaderAuthHandler.cs
        └── ToolArgumentParserTests.cs
```

---

## Architecture Principles Implemented

1. **Untrusted Model Arguments vs. Trusted Caller Context**
   - `orderId` (`integer`) comes from the model's `requires_action` tool call arguments and is re-validated in `GetOrderToolArgumentParser` (`[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`).
   - `tenant_id` (`tenant-a`, `tenant-b`) and permissions (`OrderRead`) **never** appear in the LLM tool JSON schema — they come strictly from the authenticated caller's `ClaimsPrincipal`.
2. **Gate 1 — Coarse-Grained Policy (`OrderRead`)**
   - `AuthorizationPolicies` rejects unauthenticated (`401 Unauthorized`) and unprivileged (`403 Forbidden`) callers before any database query runs.
3. **Gate 2 — Tenant-Scoped Isolation & Resource Authorization**
   - **Pattern A (`OrderReadRepository`):** Filters SQL queries by both `OrderId` and verified `callerTenantId` (`AsNoTracking().Where(o => o.OrderId == orderId && o.TenantId == callerTenantId)`), returning identical `404` (`order_unavailable`) for cross-tenant and nonexistent IDs to eliminate `403`/`404` enumeration oracles.
   - **Pattern B (`OrderAccessAuthorizationHandler`):** Imperative `IAuthorizationService` handler for intra-tenant owner/admin rules.
   - **Pattern C (`OrdersDbContext`):** EF Core Global Query Filter (`HasQueryFilter`) backed by a fail-closed sentinel (`"__unauthenticated__"`) as a defense-in-depth safety net.

---

## Quick Start & Automated Verification

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (verified on SDK `10.0.404`)

### Build & Run All Tests

```bash
dotnet test OrderAgentApi.slnx
```

Runs both unit tests (`ToolArgumentParserTests`) and live ASP.NET Core `WebApplicationFactory<Program>` + real EF Core 10 SQLite integration tests (`OrderAuthorizationMatrixTests`) covering all 4 callers (`200 OK`, `404 order_unavailable`, `401 Unauthorized`, `403 Forbidden`) and Patterns A/B/C.

### Run the API Host Locally

```bash
dotnet run --project src/OrderAgent.Api
```

*(Note: `OrderAgent.Api` intentionally defaults to a fail-closed authentication scheme in Episode 01; Episode 02 wires live Microsoft Entra ID / OIDC `JwtBearer` authentication and a persistent relational database alongside a live Microsoft Foundry agent.)*

---

## Primary References

- [Use function calling with Microsoft Foundry agents](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/function-calling)
- [Resource-based authorization in ASP.NET Core (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0)
- [Introduction to authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction)
- [OWASP API1:2023 Broken Object Level Authorization](https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/)

---

## Author & Connect

**Rehan Akbar** — Software Engineer  
- Blog: https://www.rehanakbar.dev/
- LinkedIn: https://linkedin.com/in/rehan-akbar95
- GitHub: https://github.com/rehan67
