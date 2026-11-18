using Atendee.Application.Abstractions;
using Atendee.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Atendee.Infrastructure.Persistence;

/// <summary>
/// One immutable tenant context per DI scope/request. Deliberately not registered
/// with AddDbContextPool: tenant identity must never be reused across pooled DbContexts.
/// </summary>
public sealed class TenantDbContext : DbContext
{
    private readonly TenantId _boundTenantId;

    public TenantDbContext(
        DbContextOptions<TenantDbContext> options,
        ICurrentTenantContext currentTenantContext) : base(options)
    {
        ArgumentNullException.ThrowIfNull(currentTenantContext);
        _boundTenantId = currentTenantContext.RequireTenantId();
        if (!_boundTenantId.IsValid)
            throw new InvalidOperationException("A valid authenticated tenant is required before creating TenantDbContext.");
    }

    public Guid BoundTenantId => _boundTenantId.Value;
    internal DbSet<TenantNoteRow> TenantNotes => Set<TenantNoteRow>();
    internal DbSet<TenantServiceRow> TenantServices => Set<TenantServiceRow>();
    internal DbSet<TenantProfessionalRow> TenantProfessionals => Set<TenantProfessionalRow>();
    internal DbSet<TenantProfessionalServiceRow> TenantProfessionalServices => Set<TenantProfessionalServiceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notes = modelBuilder.Entity<TenantNoteRow>();
        notes.ToTable("tenant_notes", "atendee");
        notes.HasKey(row => new { row.TenantId, row.Id });
        notes.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        notes.Property(row => row.Id).HasColumnName("note_id").IsRequired();
        notes.Property(row => row.Body).HasColumnName("body").HasMaxLength(1000).IsRequired();
        notes.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        notes.HasIndex(row => new { row.TenantId, row.CreatedAtUtc });

        var services = modelBuilder.Entity<TenantServiceRow>();
        services.ToTable("services", "atendee");
        services.HasKey(row => new { row.TenantId, row.Id });
        services.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        services.Property(row => row.Id).HasColumnName("service_id").IsRequired();
        services.Property(row => row.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        services.Property(row => row.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        services.Property(row => row.PriceMinorUnits).HasColumnName("price_minor_units").IsRequired();
        services.Property(row => row.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        services.Property(row => row.BufferBeforeMinutes).HasColumnName("buffer_before_minutes").IsRequired();
        services.Property(row => row.BufferAfterMinutes).HasColumnName("buffer_after_minutes").IsRequired();
        services.Property(row => row.IsActive).HasColumnName("is_active").IsRequired();
        services.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        services.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        services.HasIndex(row => new { row.TenantId, row.IsActive, row.Name, row.Id });
        services.HasQueryFilter(row => row.TenantId == _boundTenantId.Value);

        var professionals = modelBuilder.Entity<TenantProfessionalRow>();
        professionals.ToTable("professionals", "atendee");
        professionals.HasKey(row => new { row.TenantId, row.Id });
        professionals.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        professionals.Property(row => row.Id).HasColumnName("professional_id").IsRequired();
        professionals.Property(row => row.IdentityUserId).HasColumnName("identity_user_id").IsRequired();
        professionals.Property(row => row.IsActive).HasColumnName("is_active").IsRequired();
        professionals.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        professionals.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        professionals.HasIndex(row => new { row.TenantId, row.IsActive, row.Id });
        professionals.HasQueryFilter(row => row.TenantId == _boundTenantId.Value);

        var professionalServices = modelBuilder.Entity<TenantProfessionalServiceRow>();
        professionalServices.ToTable("professional_services", "atendee");
        professionalServices.HasKey(row => new { row.TenantId, row.ProfessionalId, row.ServiceId });
        professionalServices.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        professionalServices.Property(row => row.ProfessionalId).HasColumnName("professional_id").IsRequired();
        professionalServices.Property(row => row.ServiceId).HasColumnName("service_id").IsRequired();
        professionalServices.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        professionalServices.HasIndex(row => new { row.TenantId, row.ServiceId, row.ProfessionalId });
        professionalServices.HasQueryFilter(row => row.TenantId == _boundTenantId.Value);

        // Defense in depth only. PostgreSQL RLS remains the database boundary.
        notes.HasQueryFilter(row => row.TenantId == _boundTenantId.Value);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateTenantOwnership()
    {
        foreach (EntityEntry<TenantServiceRow> entry in ChangeTracker.Entries<TenantServiceRow>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != _boundTenantId.Value)
            {
                throw new InvalidOperationException("A tenant-scoped service does not belong to this DbContext tenant.");
            }
        }

        foreach (EntityEntry<TenantProfessionalRow> entry in ChangeTracker.Entries<TenantProfessionalRow>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != _boundTenantId.Value)
                throw new InvalidOperationException("A professional profile does not belong to this DbContext tenant.");
        }

        foreach (EntityEntry<TenantProfessionalServiceRow> entry in ChangeTracker.Entries<TenantProfessionalServiceRow>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != _boundTenantId.Value)
                throw new InvalidOperationException("A professional-service assignment does not belong to this DbContext tenant.");
        }

        foreach (EntityEntry<TenantNoteRow> entry in ChangeTracker.Entries<TenantNoteRow>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != _boundTenantId.Value)
            {
                throw new InvalidOperationException("A tenant-scoped entity does not belong to this DbContext tenant.");
            }
        }
    }
}
