namespace OrderAgent.Application.Abstractions;

/// <summary>
/// Provides the verified caller tenant identifier or a fail-closed sentinel when absent.
/// </summary>
public interface ITenantContext
{
    public const string UnauthenticatedSentinel = "__unauthenticated__";

    string VerifiedTenantId { get; }
}
