using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Domain.Professionals;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantProfessionalAvailabilityStore : ITenantProfessionalAvailabilityStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantProfessionalAvailabilityStore(
        TenantDbContext db,
        ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<IReadOnlyList<ProfessionalAvailabilityIntervalModel>> ListAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));

        return await _db.TenantProfessionalAvailability
            .AsNoTracking()
            .Where(row => row.ProfessionalId == professionalId && row.IsActive)
            .OrderBy(row => row.IsoDayOfWeek)
            .ThenBy(row => row.StartTime)
            .Select(row => new ProfessionalAvailabilityIntervalModel(
                row.Id, row.ProfessionalId, row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProfessionalAvailabilityIntervalModel>> ReplaceAsync(
        Guid professionalId,
        ProfessionalAvailabilitySchedule schedule,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));
        ArgumentNullException.ThrowIfNull(schedule);

        // Serialize full replacements for one professional. The database trigger
        // uses the same lock key to protect direct and concurrent writes.
        var lockKey = $"{_tenantId.Value:D}:{professionalId:D}";
        await _db.Database.SqlQuery<int>(
            $"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 17318))) AS availability_lock")
            .SingleAsync(cancellationToken);

        var existing = await _db.TenantProfessionalAvailability
            .Where(row => row.ProfessionalId == professionalId && row.IsActive)
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

        if (deactivatedAny)
            await _db.SaveChangesAsync(cancellationToken);

        var result = new List<ProfessionalAvailabilityIntervalModel>(schedule.Intervals.Count);
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

            var newRow = TenantProfessionalAvailabilityRow.FromDomain(
                _tenantId.Value, professionalId, interval, nowUtc);
            _db.TenantProfessionalAvailability.Add(newRow);
            result.Add(newRow.ToModel());
        }

        return result
            .OrderBy(interval => interval.IsoDayOfWeek)
            .ThenBy(interval => interval.StartTime)
            .ToArray();
    }
}
