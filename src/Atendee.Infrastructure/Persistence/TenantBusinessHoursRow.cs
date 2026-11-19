using Atendee.Application.Models;
using Atendee.Domain.Scheduling;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantBusinessHoursRow
{
    private TenantBusinessHoursRow() { } // EF Core

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public int IsoDayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantBusinessHoursRow FromDomain(
        Guid tenantId,
        BusinessHoursInterval interval,
        DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));

        return new TenantBusinessHoursRow
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            IsoDayOfWeek = interval.IsoDayOfWeek,
            StartTime = interval.StartTime,
            EndTime = interval.EndTime,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    public BusinessHoursIntervalModel ToModel() => new(
        Id, IsoDayOfWeek, StartTime, EndTime);
}
