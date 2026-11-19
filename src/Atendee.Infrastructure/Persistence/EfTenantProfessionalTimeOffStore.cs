using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Domain.Professionals;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantProfessionalTimeOffStore : ITenantProfessionalTimeOffStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantProfessionalTimeOffStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<IReadOnlyList<ProfessionalTimeOffModel>> ListAsync(
        Guid professionalId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));
        if (from > to)
            throw new ArgumentException("The time-off date range is invalid.", nameof(from));

        return await _db.TenantProfessionalTimeOff
            .AsNoTracking()
            .Where(row => row.ProfessionalId == professionalId && row.IsActive &&
                          row.Date >= from && row.Date <= to)
            .OrderBy(row => row.Date)
            .ThenBy(row => row.StartTime)
            .Select(row => new ProfessionalTimeOffModel(
                row.Id, row.ProfessionalId, row.Date, row.StartTime, row.EndTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfessionalTimeOffModel?> CreateAsync(
        Guid professionalId,
        ProfessionalTimeOffInterval interval,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));
        ArgumentNullException.ThrowIfNull(interval);

        await LockProfessionalAsync(professionalId, cancellationToken);
        var existing = await _db.TenantProfessionalTimeOff
            .Where(row => row.ProfessionalId == professionalId && row.Date == interval.Date && row.IsActive)
            .ToListAsync(cancellationToken);

        _ = ProfessionalTimeOffSchedule.Create(existing
            .Select(row => new ProfessionalTimeOffInterval(row.Date, row.StartTime, row.EndTime))
            .Append(interval));

        var nowUtc = DateTimeOffset.UtcNow;
        var row = TenantProfessionalTimeOffRow.FromDomain(_tenantId.Value, professionalId, interval, nowUtc);
        _db.TenantProfessionalTimeOff.Add(row);
        return row.ToModel();
    }

    public async Task<ProfessionalTimeOffModel?> ArchiveAsync(
        Guid professionalId,
        Guid timeOffId,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty || timeOffId == Guid.Empty)
            throw new ArgumentException("Professional and time-off IDs are required.");

        await LockProfessionalAsync(professionalId, cancellationToken);
        var row = await _db.TenantProfessionalTimeOff
            .SingleOrDefaultAsync(item => item.ProfessionalId == professionalId &&
                                          item.Id == timeOffId && item.IsActive,
                cancellationToken);
        if (row is null)
            return null;

        row.Deactivate(DateTimeOffset.UtcNow);
        return row.ToModel();
    }

    private async Task LockProfessionalAsync(Guid professionalId, CancellationToken cancellationToken)
    {
        var lockKey = $"{_tenantId.Value:D}:{professionalId:D}";
        await _db.Database.SqlQuery<int>(
            $"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 17319))) AS time_off_lock")
            .SingleAsync(cancellationToken);
    }
}
