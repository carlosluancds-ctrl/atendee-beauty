using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Professionals;
using Atendee.Domain.Primitives;
using Atendee.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantProfessionalStore : ITenantProfessionalStore
{
    private const string ProfileProjection = """
        SELECT p.professional_id AS "Id",
               p.identity_user_id AS "IdentityUserId",
               u.display_name AS "DisplayName",
               p.is_active AS "IsActive",
               p.created_at_utc AS "CreatedAtUtc",
               p.updated_at_utc AS "UpdatedAtUtc"
        FROM atendee.professionals AS p
        INNER JOIN atendee.tenant_users AS u
            ON u.tenant_id = p.tenant_id
           AND u.identity_user_id = p.identity_user_id
        """;

    private readonly TenantDbContext _db;
    private readonly TenantId _tenantId;

    public EfTenantProfessionalStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db;
        _tenantId = tenantContext.RequireTenantId();
        if (_tenantId.Value != db.BoundTenantId)
            throw new InvalidOperationException("Repository tenant does not match the DbContext tenant.");
    }

    public async Task<IReadOnlyList<ProfessionalProfileModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _db.Database.SqlQueryRaw<TenantProfessionalLookup>(
            ProfileProjection + """ WHERE p.tenant_id = {0} ORDER BY u.display_name, p.professional_id """,
            _tenantId.Value).ToListAsync(cancellationToken);
        return await AttachServiceIdsAsync(profiles, cancellationToken);
    }

    public async Task<IReadOnlyList<ProfessionalCandidateModel>> ListEligibleMembersAsync(
        CancellationToken cancellationToken = default)
    {
        var candidates = await _db.Database.SqlQueryRaw<TenantProfessionalCandidateLookup>(
            """
            SELECT u.identity_user_id AS "IdentityUserId",
                   u.display_name AS "DisplayName"
            FROM atendee.tenant_users AS u
            WHERE u.tenant_id = {0}
              AND u.role = 4
              AND u.is_active = TRUE
              AND NULLIF(BTRIM(u.display_name), '') IS NOT NULL
              AND NOT EXISTS
              (
                  SELECT 1 FROM atendee.professionals AS p
                  WHERE p.tenant_id = u.tenant_id
                    AND p.identity_user_id = u.identity_user_id
              )
            ORDER BY u.display_name, u.identity_user_id
            """,
            _tenantId.Value).ToListAsync(cancellationToken);
        return candidates.Select(candidate =>
            new ProfessionalCandidateModel(candidate.IdentityUserId, candidate.DisplayName)).ToArray();
    }

    public async Task<IReadOnlyList<SalonServiceModel>> ListOwnServicesAsync(
        Guid identityUserId, CancellationToken cancellationToken = default)
    {
        if (identityUserId == Guid.Empty)
            return [];

        var professionalId = await _db.TenantProfessionals.AsNoTracking()
            .Where(profile => profile.IdentityUserId == identityUserId && profile.IsActive)
            .Select(profile => (Guid?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (professionalId is null)
            return [];

        return await (
            from assignment in _db.TenantProfessionalServices.AsNoTracking()
            join service in _db.TenantServices.AsNoTracking()
                on new { assignment.TenantId, assignment.ServiceId }
                equals new { service.TenantId, ServiceId = service.Id }
            where assignment.ProfessionalId == professionalId.Value
            orderby service.Name, service.Id
            select new SalonServiceModel(
                service.Id, service.Name, service.DurationMinutes, service.PriceMinorUnits,
                service.Currency, service.BufferBeforeMinutes, service.BufferAfterMinutes,
                service.IsActive, service.CreatedAtUtc, service.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfessionalProfileModel?> GetOwnAsync(
        Guid identityUserId, CancellationToken cancellationToken = default)
    {
        if (identityUserId == Guid.Empty)
            return null;
        var profiles = await _db.Database.SqlQueryRaw<TenantProfessionalLookup>(
            ProfileProjection + """ WHERE p.tenant_id = {0} AND p.identity_user_id = {1} """,
            _tenantId.Value, identityUserId).ToListAsync(cancellationToken);
        var models = await AttachServiceIdsAsync(profiles, cancellationToken);
        return models.SingleOrDefault();
    }

    public async Task<ProfessionalProfileModel> CreateAsync(
        Guid identityUserId, CancellationToken cancellationToken = default)
    {
        if (identityUserId == Guid.Empty)
            throw new ArgumentException("Operational identity ID is required.", nameof(identityUserId));

        var membership = await _db.Database.SqlQueryRaw<TenantProfessionalMembershipLookup>(
            """
            SELECT role::integer AS "Role", is_active AS "IsActive", display_name AS "DisplayName"
            FROM atendee.tenant_users
            WHERE tenant_id = {0} AND identity_user_id = {1}
            """, _tenantId.Value, identityUserId).SingleOrDefaultAsync(cancellationToken);

        if (membership is null || membership.Role != 4 || !membership.IsActive ||
            string.IsNullOrWhiteSpace(membership.DisplayName))
            throw new ArgumentException("An active Professional membership in the current salon is required.", nameof(identityUserId));

        var profile = ProfessionalProfile.Create(Guid.NewGuid(), identityUserId, DateTimeOffset.UtcNow);
        _db.TenantProfessionals.Add(TenantProfessionalRow.FromDomain(_tenantId.Value, profile));
        return new ProfessionalProfileModel(
            profile.Id, identityUserId, membership.DisplayName, profile.IsActive, [],
            profile.CreatedAtUtc, profile.UpdatedAtUtc);
    }

    public async Task<ProfessionalProfileModel?> SetServicesAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty)
            return null;
        if (serviceIds.Any(id => id == Guid.Empty) || serviceIds.Distinct().Count() != serviceIds.Count)
            throw new ArgumentException("Service IDs must be unique, non-empty values.", nameof(serviceIds));

        var profile = await _db.TenantProfessionals.SingleOrDefaultAsync(
            item => item.Id == professionalId, cancellationToken);
        if (profile is null)
            return null;

        if (serviceIds.Count > 0)
        {
            var activeCount = await _db.TenantServices.CountAsync(
                service => serviceIds.Contains(service.Id) && service.IsActive,
                cancellationToken);
            if (activeCount != serviceIds.Count)
                throw new ArgumentException("Every assigned service must be active and belong to the current salon.", nameof(serviceIds));
        }

        var existing = await _db.TenantProfessionalServices
            .Where(item => item.ProfessionalId == professionalId)
            .ToListAsync(cancellationToken);
        _db.TenantProfessionalServices.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var serviceId in serviceIds)
        {
            _db.TenantProfessionalServices.Add(
                TenantProfessionalServiceRow.Create(_tenantId.Value, professionalId, serviceId, now));
        }

        var lookup = await _db.Database.SqlQueryRaw<TenantProfessionalLookup>(
            ProfileProjection + """ WHERE p.tenant_id = {0} AND p.professional_id = {1} """,
            _tenantId.Value, professionalId).SingleAsync(cancellationToken);
        return new ProfessionalProfileModel(
            lookup.Id, lookup.IdentityUserId, lookup.DisplayName, lookup.IsActive,
            serviceIds.Order().ToArray(), lookup.CreatedAtUtc, lookup.UpdatedAtUtc);
    }

    private async Task<IReadOnlyList<ProfessionalProfileModel>> AttachServiceIdsAsync(
        IReadOnlyList<TenantProfessionalLookup> profiles,
        CancellationToken cancellationToken)
    {
        if (profiles.Count == 0)
            return [];
        var ids = profiles.Select(profile => profile.Id).ToArray();
        var assignments = await _db.TenantProfessionalServices.AsNoTracking()
            .Where(row => ids.Contains(row.ProfessionalId))
            .OrderBy(row => row.ServiceId)
            .ToListAsync(cancellationToken);
        return profiles.Select(profile => new ProfessionalProfileModel(
            profile.Id, profile.IdentityUserId, profile.DisplayName, profile.IsActive,
            assignments.Where(row => row.ProfessionalId == profile.Id)
                .Select(row => row.ServiceId).ToArray(),
            profile.CreatedAtUtc, profile.UpdatedAtUtc)).ToArray();
    }
}
