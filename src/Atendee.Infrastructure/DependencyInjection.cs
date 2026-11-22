using Atendee.Application.Abstractions;
using Atendee.Infrastructure.Identity;
using Atendee.Infrastructure.Messaging;
using Atendee.Infrastructure.Persistence;
using Atendee.Infrastructure.Security;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

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
        services.AddScoped<ISecurityAuditWriter, PostgresSecurityAuditWriter>();
        services.AddScoped<OperationalIdentityActivationService>();
        services.AddScoped<OperationalIdentityProvisioningService>();
        services.AddScoped<TrustedRequestContext>();
        services.AddScoped<ICurrentTenantContext>(provider =>
            provider.GetRequiredService<TrustedRequestContext>());
        services.AddScoped<ICurrentActorContext>(provider =>
            provider.GetRequiredService<TrustedRequestContext>());

        return services;
    }

    public static IServiceCollection AddAtendeeIdentity(
        this IServiceCollection services,
        string identityConnectionString,
        byte[] partitionKeySecret)
    {
        ArgumentNullException.ThrowIfNull(partitionKeySecret);
        if (partitionKeySecret.Length < 32)
            throw new ArgumentException(
                "The authentication rate-limit partition key must contain at least 32 bytes.",
                nameof(partitionKeySecret));

        services.AddAtendeeIdentity(identityConnectionString);

        var limiterConnectionString = new NpgsqlConnectionStringBuilder(identityConnectionString)
        {
            Timeout = 3,
            CommandTimeout = 3
        }.ConnectionString;
        services.AddSingleton(_ => NpgsqlDataSource.Create(limiterConnectionString));
        services.AddSingleton(new AuthenticationRateLimitPartitionKey(partitionKeySecret));
        services.AddSingleton<IDistributedAuthenticationRateLimiter, PostgresDistributedAuthenticationRateLimiter>();
        services.AddHostedService<DistributedAuthenticationRateLimitCleanupService>();

        return services;
    }

    /// <summary>
    /// Registers the readiness probe used by <c>/health/ready</c>. It reuses the shared
    /// identity <see cref="NpgsqlDataSource"/> registered by the rate-limiting overload of
    /// <c>AddAtendeeIdentity</c>, so that overload must be called first.
    /// </summary>
    public static IServiceCollection AddAtendeeDatabaseReadiness(
        this IServiceCollection services,
        string tenantConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);

        services.AddSingleton<IDatabaseReadinessProbe>(serviceProvider =>
            new NpgsqlDatabaseReadinessProbe(
                serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                tenantConnectionString,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NpgsqlDatabaseReadinessProbe>>()));

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
        services.AddScoped<ITenantServiceStore, EfTenantServiceStore>();
        services.AddScoped<ITenantCustomerStore, EfTenantCustomerStore>();
        services.AddScoped<ITenantBusinessHoursStore, EfTenantBusinessHoursStore>();
        services.AddScoped<ITenantProfessionalAvailabilityStore, EfTenantProfessionalAvailabilityStore>();
        services.AddScoped<ITenantProfessionalTimeOffStore, EfTenantProfessionalTimeOffStore>();
        services.AddScoped<ITenantAppointmentStore, EfTenantAppointmentStore>();
        services.AddScoped<ITenantProfessionalStore, EfTenantProfessionalStore>();
        return services;
    }

    /// <summary>
    /// Registers the transactional outbox dispatcher with RabbitMQ.
    /// Requires AddAtendeePersistence to be called first for TenantDbContext.
    /// </summary>
    public static IServiceCollection AddAtendeeOutboxDispatcher(
        this IServiceCollection services,
        Action<OutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OutboxDispatcherOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(options.ConnectionString));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TenantConnectionString, nameof(options.TenantConnectionString));

        var tenantConnStr = options.TenantConnectionString;

        services.Configure<OutboxDispatcherOptions>(opt =>
        {
            opt.ConnectionString = options.ConnectionString;
            opt.ExchangeName = options.ExchangeName;
            opt.ExchangeType = options.ExchangeType;
            opt.PollInterval = options.PollInterval;
            opt.BatchSize = options.BatchSize;
            opt.MaxRetryAttempts = options.MaxRetryAttempts;
            opt.BaseRetryDelay = options.BaseRetryDelay;
            opt.MaxRetryDelay = options.MaxRetryDelay;
            opt.DeadLetterExchange = options.DeadLetterExchange;
            opt.DeadLetterQueue = options.DeadLetterQueue;
            opt.TenantConnectionString = options.TenantConnectionString;
        });

        // Register OutboxDbContext for the dispatcher (no tenant isolation)
        services.AddDbContext<OutboxDbContext>(opt =>
            opt.UseNpgsql(tenantConnStr, npgsql => npgsql.SetPostgresVersion(18, 7)),
            contextLifetime: ServiceLifetime.Scoped);

        services.AddHostedService<OutboxDispatcher>();

        return services;
    }

    /// <summary>
    /// Registers the coordinated Data Protection key rotation service.
    /// </summary>
    public static IServiceCollection AddAtendeeDataProtectionKeyRotation(
        this IServiceCollection services,
        Action<DataProtectionKeyRotationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddHostedService<DataProtectionKeyRotationService>();

        return services;
    }

    /// <summary>
    /// Registers the Resend email sender.
    /// </summary>
    public static IServiceCollection AddAtendeeResendEmail(
        this IServiceCollection services,
        Action<ResendEmailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ResendEmailOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey, nameof(options.ApiKey));

        services.Configure<ResendEmailOptions>(opt =>
        {
            opt.ApiKey = options.ApiKey;
            opt.FromEmail = options.FromEmail;
            opt.FromName = options.FromName;
        });

        services.AddHttpClient<ResendEmailSender>();
        services.AddScoped<IEmailSender, ResendEmailSender>();

        return services;
    }

    /// <summary>
    /// Configuration for the Resend email sender.
    /// </summary>
    public sealed class ResendEmailOptions
    {
        public const string SectionName = "Resend";

        public string ApiKey { get; set; } = string.Empty;
        public string FromEmail { get; set; } = "Atendee Beauty <noreply@atendee.local>";
        public string FromName { get; set; } = "Atendee Beauty";
    }

    /// <summary>
    /// Registers the Platform Admin service for cross-tenant administration.
    /// </summary>
    public static IServiceCollection AddAtendeePlatformAdmin(
        this IServiceCollection services,
        Action<PlatformAdminOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddScoped<IPlatformAdminService, PlatformAdminService>();

        return services;
    }
}