using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Atendee.Infrastructure.Security;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Stores operational login credentials in the isolated identity schema.
/// Tenant-owned business data is not mapped by this context.
/// TOTP authenticator keys and recovery codes are protected at rest by
/// ASP.NET Core Data Protection through the Identity token value converter.
/// </summary>
public sealed class AtendeeIdentityDbContext(
    DbContextOptions<AtendeeIdentityDbContext> options,
    IDataProtectionProvider dataProtectionProvider)
    : IdentityUserContext<OperationalIdentityUser, Guid>(options)
{
    internal DbSet<SecurityAuditEventRow> SecurityAuditEvents => Set<SecurityAuditEventRow>();
    internal DbSet<OperationalIdentityActivationTokenRow> OperationalIdentityActivationTokens
        => Set<OperationalIdentityActivationTokenRow>();
    internal DbSet<PlatformAdminEntity> PlatformAdmins => Set<PlatformAdminEntity>();
    internal DbSet<FourEyesAuthorizationEntity> FourEyesAuthorizations => Set<FourEyesAuthorizationEntity>();
    internal DbSet<PlatformAdminAuditLogEntity> PlatformAdminAuditLogs => Set<PlatformAdminAuditLogEntity>();
    internal DbSet<TenantProfileEntity> TenantProfiles => Set<TenantProfileEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");

        var audit = builder.Entity<SecurityAuditEventRow>();
        audit.ToTable("SecurityAuditEvents", "identity");
        audit.HasKey(row => row.Id).HasName("PK_SecurityAuditEvents");
        audit.Property(row => row.Id).HasColumnName("Id");
        audit.Property(row => row.EventType).HasColumnName("EventType").HasMaxLength(80).IsRequired();
        audit.Property(row => row.ActorIdentityUserId).HasColumnName("ActorIdentityUserId");
        audit.Property(row => row.TargetIdentityUserId).HasColumnName("TargetIdentityUserId").IsRequired();
        audit.Property(row => row.OccurredAtUtc).HasColumnName("OccurredAtUtc").IsRequired();
        audit.Property(row => row.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(64);

        var activation = builder.Entity<OperationalIdentityActivationTokenRow>();
        activation.ToTable("OperationalIdentityActivationTokens", "identity");
        activation.HasKey(row => row.UserId).HasName("PK_OperationalIdentityActivationTokens");
        activation.Property(row => row.UserId).HasColumnName("UserId");
        activation.Property(row => row.TokenHash).HasColumnName("TokenHash").HasColumnType("bytea").IsRequired();
        activation.Property(row => row.CreatedAtUtc).HasColumnName("CreatedAtUtc").IsRequired();
        activation.Property(row => row.ExpiresAtUtc).HasColumnName("ExpiresAtUtc").IsRequired();
        activation.Property(row => row.ConsumedAtUtc).HasColumnName("ConsumedAtUtc");
        activation.HasIndex(row => row.TokenHash).IsUnique()
            .HasDatabaseName("UX_OperationalIdentityActivationTokens_TokenHash");
        activation.HasOne<OperationalIdentityUser>()
            .WithMany()
            .HasForeignKey(row => row.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The first release intentionally uses one globally unique email per
        // operational identity to prevent ambiguous multi-salon logins.
        builder.Entity<OperationalIdentityUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("EmailIndex")
            .HasFilter("\"NormalizedEmail\" IS NOT NULL");

        var tokenProtector = dataProtectionProvider.CreateProtector(
            "AtendeeBeauty.IdentityUserToken.Value.v1");

        // Identity stores authenticator keys and recovery codes as user tokens.
        // Encrypt every token value in the EF persistence boundary so plaintext
        // TOTP secrets/recovery codes are never stored in PostgreSQL.
        builder.Entity<IdentityUserToken<Guid>>()
            .Property(token => token.Value)
            .HasConversion(new ValueConverter<string?, string?>(
                value => value == null ? null : tokenProtector.Protect(value),
                value => value == null ? null : tokenProtector.Unprotect(value)));

        // Platform Admin entities
        var platformAdmin = builder.Entity<PlatformAdminEntity>();
        platformAdmin.ToTable("PlatformAdmins", "identity");
        platformAdmin.HasKey(p => p.IdentityUserId).HasName("PK_PlatformAdmins");
        platformAdmin.Property(p => p.IdentityUserId).HasColumnName("identity_user_id").IsRequired();
        platformAdmin.Property(p => p.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        platformAdmin.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        platformAdmin.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        platformAdmin.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        platformAdmin.Property(p => p.LastMfaChallengeUtc).HasColumnName("last_mfa_challenge_utc");
        platformAdmin.Property(p => p.CreatedByIdentityUserId).HasColumnName("created_by_identity_user_id").IsRequired();
        platformAdmin.Property(p => p.RemovedAtUtc).HasColumnName("removed_at_utc");
        platformAdmin.Property(p => p.RemovedByIdentityUserId).HasColumnName("removed_by_identity_user_id");
        platformAdmin.HasIndex(p => p.Email).IsUnique().HasDatabaseName("UX_PlatformAdmins_Email");
        platformAdmin.HasIndex(p => p.IsActive).HasDatabaseName("IX_PlatformAdmins_IsActive");

        var fourEyes = builder.Entity<FourEyesAuthorizationEntity>();
        fourEyes.ToTable("FourEyesAuthorizations", "identity");
        fourEyes.HasKey(f => f.RequestId).HasName("PK_FourEyesAuthorizations");
        fourEyes.Property(f => f.RequestId).HasColumnName("request_id").IsRequired();
        fourEyes.Property(f => f.InitiatorIdentityUserId).HasColumnName("initiator_identity_user_id").IsRequired();
        fourEyes.Property(f => f.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        fourEyes.Property(f => f.ResourceType).HasColumnName("resource_type").HasMaxLength(50).IsRequired();
        fourEyes.Property(f => f.ResourceId).HasColumnName("resource_id").HasMaxLength(256).IsRequired();
        fourEyes.Property(f => f.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        fourEyes.Property(f => f.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        fourEyes.Property(f => f.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        fourEyes.Property(f => f.Status).HasColumnName("status").IsRequired();
        fourEyes.Property(f => f.ApproverIdentityUserId).HasColumnName("approver_identity_user_id");
        fourEyes.Property(f => f.ApprovedAtUtc).HasColumnName("approved_at_utc");
        fourEyes.HasIndex(f => f.Status).HasDatabaseName("IX_FourEyesAuthorizations_Status");
        fourEyes.HasIndex(f => f.ExpiresAtUtc).HasDatabaseName("IX_FourEyesAuthorizations_ExpiresAt");

        var auditLog = builder.Entity<PlatformAdminAuditLogEntity>();
        auditLog.ToTable("PlatformAdminAuditLogs", "identity");
        auditLog.HasKey(a => a.AuditId).HasName("PK_PlatformAdminAuditLogs");
        auditLog.Property(a => a.AuditId).HasColumnName("audit_id").IsRequired();
        auditLog.Property(a => a.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        auditLog.Property(a => a.ActorIdentityUserId).HasColumnName("actor_identity_user_id").IsRequired();
        auditLog.Property(a => a.ActorEmail).HasColumnName("actor_email").HasMaxLength(256).IsRequired();
        auditLog.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        auditLog.Property(a => a.ResourceType).HasColumnName("resource_type").HasMaxLength(50).IsRequired();
        auditLog.Property(a => a.ResourceId).HasColumnName("resource_id").HasMaxLength(256).IsRequired();
        auditLog.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500);
        auditLog.Property(a => a.Metadata).HasColumnName("metadata").HasMaxLength(2000);
        auditLog.Property(a => a.Success).HasColumnName("success").IsRequired();
        auditLog.Property(a => a.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
        auditLog.HasIndex(a => a.OccurredAtUtc).HasDatabaseName("IX_PlatformAdminAuditLogs_OccurredAt");
        auditLog.HasIndex(a => a.ActorIdentityUserId).HasDatabaseName("IX_PlatformAdminAuditLogs_Actor");
        auditLog.HasIndex(a => a.Action).HasDatabaseName("IX_PlatformAdminAuditLogs_Action");

        // Tenant profiles (cross-tenant read for Platform Admin)
        var tenantProfile = builder.Entity<TenantProfileEntity>();
        tenantProfile.ToTable("tenants", "atendee").HasNoKey();
        tenantProfile.HasKey(t => t.TenantId).HasName("PK_Tenants");
        tenantProfile.Property(t => t.TenantId).HasColumnName("id").IsRequired();
        tenantProfile.Property(t => t.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        tenantProfile.Property(t => t.Subdomain).HasColumnName("slug").HasMaxLength(60).IsRequired();
        tenantProfile.Property(t => t.Status).HasColumnName("status").IsRequired();
        tenantProfile.Property(t => t.AdminCount).HasColumnName("admin_count").IsRequired();
        tenantProfile.Property(t => t.ProfessionalCount).HasColumnName("professional_count").IsRequired();
        tenantProfile.Property(t => t.CustomerCount).HasColumnName("customer_count").IsRequired();
        tenantProfile.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        tenantProfile.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        tenantProfile.HasIndex(t => t.Status).HasDatabaseName("IX_Tenants_Status");
        tenantProfile.HasIndex(t => t.Subdomain).IsUnique().HasDatabaseName("UX_Tenants_Slug");
    }
}