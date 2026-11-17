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
