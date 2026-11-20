using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

/// <summary>
/// DbContext for the transactional outbox dispatcher.
/// Does not apply tenant isolation - the dispatcher polls across all tenants.
/// </summary>
public sealed class OutboxDbContext : DbContext
{
    public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

    internal DbSet<OutboxMessageRow> TransactionalOutbox => Set<OutboxMessageRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<OutboxMessageRow>();
        outbox.ToTable("transactional_outbox", "atendee");
        outbox.HasKey(row => new { row.TenantId, row.MessageId });
        outbox.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        outbox.Property(row => row.MessageId).HasColumnName("message_id").IsRequired();
        outbox.Property(row => row.AggregateId).HasColumnName("aggregate_id").IsRequired();
        outbox.Property(row => row.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        outbox.Property(row => row.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        outbox.Property(row => row.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        outbox.Property(row => row.AvailableAtUtc).HasColumnName("available_at_utc").IsRequired();
        outbox.Property(row => row.ProcessedAtUtc).HasColumnName("processed_at_utc");
        outbox.Property(row => row.Attempts).HasColumnName("attempts").IsRequired();
    }
}

/// <summary>
/// Row representation of a transactional outbox message.
/// </summary>
internal sealed class OutboxMessageRow
{
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
}