namespace OrderAgent.Domain.Entities;

/// <summary>
/// Represents an order record scoped by tenant and owning customer.
/// </summary>
public sealed class Order
{
    public int OrderId { get; set; }
    public required string TenantId { get; set; }
    public required string CustomerUserId { get; set; }
    public required string Status { get; set; }
}
