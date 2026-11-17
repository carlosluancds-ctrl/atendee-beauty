using Atendee.Application.Abstractions;
using Atendee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atendee.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtendeePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Do not use AddDbContextPool: tenant identity is captured once per scoped DbContext.
        services.AddDbContext<TenantDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.SetPostgresVersion(18, 7)));

        services.AddScoped<ITenantTransactionRunner, NpgsqlTenantTransactionRunner>();
        services.AddScoped<ITenantNoteStore, EfTenantNoteStore>();
        return services;
    }
}
