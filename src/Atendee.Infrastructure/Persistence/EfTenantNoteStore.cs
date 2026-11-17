using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantNoteStore : ITenantNoteStore
{
    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantNoteStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public Task AddAsync(Guid id, string body, CancellationToken cancellationToken = default)
    {
        var row = TenantNoteRow.Create(_tenantId.Value, id, body, DateTimeOffset.UtcNow);
        _db.TenantNotes.Add(row);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TenantNote>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.TenantNotes
            .AsNoTracking()
            .OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new TenantNote(row.Id, row.Body, row.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
    public async Task<TenantNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.TenantNotes.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new TenantNote(row.Id, row.Body, row.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

}
