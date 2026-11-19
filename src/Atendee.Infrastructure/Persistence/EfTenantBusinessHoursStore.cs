using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantBusinessHoursStore : ITenantBusinessHoursStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantBusinessHoursStore(
        TenantDbContext db,
        ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<IReadOnlyList<BusinessHoursIntervalModel>> ListAsync(
        CancellationToken cancellationToken = default)
        => await _db.TenantBusinessHours
            .AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.IsoDayOfWeek)
            .ThenBy(row => row.StartTime)
            .Select(row => new BusinessHoursIntervalModel(
                row.Id, row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BusinessHoursIntervalModel>> ReplaceAsync(
        BusinessHoursSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        // The transaction-scoped lock serializes complete schedule replacements,
        // not just individual row inserts. The database trigger takes the same
        // lock for direct writes and enforces non-overlap independently.
        var tenantIdText = _tenantId.Value.ToString("D");
        await _db.Database.SqlQuery<int>(
            $"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({tenantIdText}, 17317))) AS tenant_lock")
            .SingleAsync(cancellationToken);

        var existing = await _db.TenantBusinessHours
            .Where(row => row.IsActive)
            .ToListAsync(cancellationToken);

        var desiredKeys = schedule.Intervals
            .Select(interval => (interval.IsoDayOfWeek, interval.StartTime, interval.EndTime))
            .ToHashSet();
        var nowUtc = DateTimeOffset.UtcNow;
        var deactivatedAny = false;

        foreach (var row in existing)
        {
            var key = (row.IsoDayOfWeek, row.StartTime, row.EndTime);
            if (!desiredKeys.Contains(key))
            {
                row.Deactivate(nowUtc);
                deactivatedAny = true;
            }
        }

        // Flush deactivations before inserting replacements so the overlap
        // trigger sees the new schedule within this same outer transaction.
        if (deactivatedAny)
            await _db.SaveChangesAsync(cancellationToken);

        var result = new List<BusinessHoursIntervalModel>(schedule.Intervals.Count);
        foreach (var interval in schedule.Intervals)
        {
            var existingRow = existing.FirstOrDefault(row =>
                row.IsActive &&
                row.IsoDayOfWeek == interval.IsoDayOfWeek &&
                row.StartTime == interval.StartTime &&
                row.EndTime == interval.EndTime);

            if (existingRow is not null)
            {
                result.Add(existingRow.ToModel());
                continue;
            }

            var newRow = TenantBusinessHoursRow.FromDomain(_tenantId.Value, interval, nowUtc);
            _db.TenantBusinessHours.Add(newRow);
            result.Add(newRow.ToModel());
        }

        return result
            .OrderBy(interval => interval.IsoDayOfWeek)
            .ThenBy(interval => interval.StartTime)
            .ToArray();
    }
}
