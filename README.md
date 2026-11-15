# How Microsoft Foundry Agents Call Tools Safely (.NET 10)

Companion source code and engineering walkthrough for **Episode 01** of **Microsoft Foundry: Zero to Production** by **Rehan Akbar**.

Demonstrates why authentication alone is insufficient when an AI agent proposes tool calls (`get_order(orderId)`), and how to enforce **multi-tenant authorization** inside an **ASP.NET Core (.NET 10)** backend using a **Two-Gate Authorization Model** before querying data.

---

## Architecture Overview

1. **Untrusted Model Arguments vs. Trusted Caller Context**
   - `orderId` (`integer`) comes from the model's `requires_action` tool call arguments.
   - `tenant_id` (`tenant-a`, `tenant-b`) and permissions (`OrderRead`) **never** belong in the LLM tool JSON schema — they come strictly from the authenticated caller's `ClaimsPrincipal`.
2. **Gate 1 — Coarse-Grained Policy (`OrderRead`)**
   - Rejects unauthenticated (`401 Unauthorized`) and unprivileged (`403 Forbidden`) callers before any database query runs.
3. **Gate 2 — Tenant-Scoped Isolation & Resource Authorization**
   - **Pattern A:** Filters SQL queries by both `OrderId` and verified `callerTenantId` (`AsNoTracking().Where(o => o.OrderId == orderId && o.TenantId == callerTenantId)`), returning identical `404` (`order_unavailable`) for cross-tenant and nonexistent IDs to eliminate `403`/`404` enumeration oracles.
   - **Pattern B:** Imperative resource-based authorization handler (`OrderAccessAuthorizationHandler` + `IAuthorizationService`) for intra-tenant owner/admin rules.
   - **Pattern C:** EF Core Global Query Filter (`HasQueryFilter`) backed by a fail-closed sentinel (`"__unauthenticated__"`) as a defense-in-depth safety net.

---

## Repository Structure

```text
.
├── README.md
├── docs/
│   └── blog.md                     # Complete Episode 01 technical walkthrough
└── src/
    └── OrderAgentApi/
        ├── OrderAgentApi.csproj    # .NET 10 Web API + EF Core 10 SQLite
        └── Program.cs              # Self-verifying ASP.NET Core 10 harness (15 assertions, 4 callers)
```

---

## Quick Start & Self-Verification

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (verified on SDK `10.0.404`, runtime `10.0.15`)

### Run All Verification Checks

```bash
cd src/OrderAgentApi
dotnet run
```

Running `dotnet run` boots ASP.NET Core Kestrel on an ephemeral loopback port with an in-memory SQLite database (`Microsoft.EntityFrameworkCore.Sqlite 10.0.15`), runs `15` automated assertions across Patterns A/B/C and the 4 caller boundaries (`Owner`, `Other Tenant`, `Signed Out`, `Missing Role`), and exits `0`:

```text
ALL .NET 10.0.15 + EF CORE 10 SQLITE CHECKS PASSED (15/15 assertions, 4/4 callers, Patterns A/B/C verified).
```

---

## What Is Verified vs. Episode 02 Scope

- **Verified locally in this repo:** `TryParseOrderId` (`JsonUnmappedMemberHandling.Disallow` enforcing `"additionalProperties": false`), `"OrderRead"` policy gate, `/orders/{orderId:int}` SQLite EF Core query translation, oracle-safe `404` (`order_unavailable`), `OrderAccessAuthorizationHandler`, fail-closed EF Core Global Query Filter, and all 4 callers (`200`, `404`, `401`, `403`) using a local test header auth scheme.
- **Episode 02 (upcoming):** Connecting a live Microsoft Foundry agent tool-calling loop end to end with persistent database integration tests.

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
