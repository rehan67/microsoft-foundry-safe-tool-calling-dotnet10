# Microsoft Foundry: Safe Agent Tool Calling & Secure .NET 10 Architecture

Companion source code and engineering walkthroughs for **Episode 01** and **Episode 02** of **Microsoft Foundry: Zero to Production** by **Rehan Akbar**.

Demonstrates why authentication alone is insufficient when an AI agent proposes tool calls (`get_order(orderId)`), and how to build and verify a **multi-tenant ASP.NET Core (.NET 10)** backend structured with **Clean Architecture**, **Two-Gate Authorization**, **EF Core 10 Query Filters**, and a **4-Caller `WebApplicationFactory` Test Matrix**.

### Episodes Covered in This Repository

- **Episode 01 — How Microsoft Foundry AI Agents Actually Work:** [Full Engineering Walkthrough (`docs/blog.md`)](docs/blog.md)
- **Episode 02 — Microsoft Foundry + .NET 10: Secure Agent Architecture:** [Full Engineering Walkthrough (`docs/episode-02-secure-agent-architecture.md`)](docs/episode-02-secure-agent-architecture.md)

---

## Clean Architecture Layout

```text
.
├── .gitignore
├── README.md
├── OrderAgentApi.slnx
├── docs/
│   ├── blog.md                                          # Episode 01 deep-dive walkthrough
│   └── episode-02-secure-agent-architecture.md          # Episode 02 .NET 10 architecture & 4-caller matrix walkthrough
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

_(Note: `OrderAgent.Api` intentionally defaults to a fail-closed authentication scheme when started standalone; `OrderAuthorizationMatrixTests` boots the full in-memory ASP.NET Core `.NET 10` pipeline via `WebApplicationFactory<Program>` and tests all 4 caller states against real EF Core 10 SQLite tables.)_

---

## Primary References

- [Use function calling with Microsoft Foundry agents](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/function-calling)
- [Resource-based authorization in ASP.NET Core (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0)
- [Global & Named Query Filters in EF Core 10](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [Integration tests in ASP.NET Core (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [OWASP API1:2023 Broken Object Level Authorization](https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/)

---

## Author & Connect

**Rehan Akbar** — Software Engineer

- Blog: https://www.rehanakbar.dev/
- LinkedIn: https://linkedin.com/in/rehan-akbar95
- GitHub: https://github.com/rehan67
