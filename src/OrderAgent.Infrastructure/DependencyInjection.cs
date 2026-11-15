using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderAgent.Application.Abstractions;
using OrderAgent.Application.UseCases;
using OrderAgent.Infrastructure.Persistence;
using OrderAgent.Infrastructure.Security;

namespace OrderAgent.Infrastructure;

/// <summary>
/// Registers EF Core SQLite persistence, repository implementations, and resource auth handlers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOrderAgentInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();

        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });

        services.AddDbContext<OrdersDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));

        services.AddScoped<IOrderReadRepository, OrderReadRepository>();
        services.AddScoped<GetOrderStatusHandler>();
        services.AddSingleton<IAuthorizationHandler, OrderAccessAuthorizationHandler>();

        return services;
    }
}
