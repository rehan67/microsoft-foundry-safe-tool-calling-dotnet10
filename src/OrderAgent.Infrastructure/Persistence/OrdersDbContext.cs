using Microsoft.EntityFrameworkCore;
using OrderAgent.Application.Abstractions;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext enforcing composite indexing and a fail-closed global tenant filter.
/// </summary>
public sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.OrderId);
            entity.HasIndex(o => new { o.TenantId, o.OrderId }).IsUnique();
            entity.HasQueryFilter(o => o.TenantId == _tenantContext.VerifiedTenantId);
        });
    }
}
