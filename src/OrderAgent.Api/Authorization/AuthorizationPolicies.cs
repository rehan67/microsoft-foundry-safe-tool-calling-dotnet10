namespace OrderAgent.Api.Authorization;

/// <summary>
/// Centralizes Gate 1 policy definitions for order tool endpoints.
/// </summary>
public static class AuthorizationPolicies
{
    public const string OrderRead = "OrderRead";

    public static IServiceCollection AddOrderAgentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(OrderRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.HasClaim("scope", "orders.read") ||
                    context.User.IsInRole("OrderViewer") ||
                    context.User.IsInRole("Admin"));
            });
        });

        return services;
    }
}
