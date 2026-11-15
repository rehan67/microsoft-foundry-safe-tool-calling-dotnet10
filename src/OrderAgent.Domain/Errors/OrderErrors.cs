namespace OrderAgent.Domain.Errors;

/// <summary>
/// Standardized error codes that do not reveal cross-tenant resource existence.
/// </summary>
public static class OrderErrors
{
    public const string UnavailableCode = "order_unavailable";
}
