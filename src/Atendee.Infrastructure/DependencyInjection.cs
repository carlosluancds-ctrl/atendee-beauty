using Atendee.Application.Abstractions;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Persistence;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atendee.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtendeeIdentity(
        this IServiceCollection services,
        string identityConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityConnectionString);

        services.AddDataProtection()
            .SetApplicationName("AtendeeBeauty");

        services.AddDbContext<AtendeeIdentityDbContext>(options =>
            options.UseNpgsql(
                identityConnectionString,
                npgsql => npgsql.SetPostgresVersion(18, 7)));

        services.AddIdentityCore<OperationalIdentityUser>(options =>
        {
            // Version 2 matches the explicit identity schema migration below.
            // Passkey/WebAuthn tables will be added by a later reviewed migration.
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
            options.Tokens.AuthenticatorTokenProvider = "AtendeeTOTP";
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

            // Password validation uses Identity's mature hasher. A breached-
            // password blocklist must be added before public enrollment.
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddSignInManager()
        .AddDefaultTokenProviders()
        .AddTokenProvider<OperationalTotpTokenProvider>("AtendeeTOTP")
        .AddEntityFrameworkStores<AtendeeIdentityDbContext>();

        services.AddScoped<ITenantMembershipResolver, PostgresTenantMembershipResolver>();
        services.AddScoped<IOperationalAccountSecurityService, OperationalAccountSecurityService>();
        services.AddScoped<TrustedRequestContext>();
        services.AddScoped<ICurrentTenantContext>(provider =>
            provider.GetRequiredService<TrustedRequestContext>());
        services.AddScoped<ICurrentActorContext>(provider =>
            provider.GetRequiredService<TrustedRequestContext>());

        return services;
    }

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
