using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Catalog;
using Atendee.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantServiceStore : ITenantServiceStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantServiceStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<IReadOnlyList<SalonServiceModel>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.TenantServices.AsNoTracking()
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => new SalonServiceModel(
                row.Id, row.Name, row.DurationMinutes, row.PriceMinorUnits, row.Currency,
                row.BufferBeforeMinutes, row.BufferAfterMinutes, row.IsActive,
                row.CreatedAtUtc, row.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<SalonServiceModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.TenantServices.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new SalonServiceModel(
                row.Id, row.Name, row.DurationMinutes, row.PriceMinorUnits, row.Currency,
                row.BufferBeforeMinutes, row.BufferAfterMinutes, row.IsActive,
                row.CreatedAtUtc, row.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<SalonServiceModel> CreateAsync(
        string name, int durationMinutes, long priceMinorUnits, string currency,
        int bufferBeforeMinutes, int bufferAfterMinutes, bool isActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = SalonService.Create(
            Guid.NewGuid(), name, durationMinutes, priceMinorUnits, currency,
            bufferBeforeMinutes, bufferAfterMinutes, isActive, DateTimeOffset.UtcNow);
        _db.TenantServices.Add(TenantServiceRow.FromDomain(_tenantId.Value, service));
        return Task.FromResult(ToModel(service));
    }

    public async Task<SalonServiceModel?> UpdateAsync(
        Guid id, string name, int durationMinutes, long priceMinorUnits, string currency,
        int bufferBeforeMinutes, int bufferAfterMinutes, bool isActive,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantServices.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (row is null)
            return null;

        var updated = row.ToDomain().Update(
            name, durationMinutes, priceMinorUnits, currency,
            bufferBeforeMinutes, bufferAfterMinutes, isActive, DateTimeOffset.UtcNow);
        row.Apply(updated);
        return ToModel(updated);
    }

    private static SalonServiceModel ToModel(SalonService service) => new(
        service.Id, service.Name, service.DurationMinutes, service.PriceMinorUnits,
        service.Currency, service.BufferBeforeMinutes, service.BufferAfterMinutes,
        service.IsActive, service.CreatedAtUtc, service.UpdatedAtUtc);
}
