using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    }
}
