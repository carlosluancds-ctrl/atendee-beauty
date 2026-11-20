using System.Security.Cryptography;
using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Atendee.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atendee.Infrastructure.Security;

/// <summary>
/// Configuration for Platform Admin.
/// </summary>
public sealed class PlatformAdminOptions
{
    public const string SectionName = "PlatformAdmin";

    /// <summary>
    /// Enable Platform Admin endpoints.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Required MFA for all Platform Admin actions (step-up).
    /// </summary>
    public bool RequireMfaForAllActions { get; set; } = true;

    /// <summary>
    /// Require four-eyes (dual authorization) for critical actions.
    /// </summary>
    public bool RequireFourEyesForCriticalActions { get; set; } = true;

    /// <summary>
    /// List of critical actions requiring four-eyes.
    /// </summary>
    public IReadOnlyList<string> CriticalActions { get; set; } = new[]
    {
        "tenant.delete",
        "tenant.suspend",
        "tenant.unsuspend",
        "platform_admin.create",
        "platform_admin.delete",
        "global.config.change"
    };

    /// <summary>
    /// Self/last-Owner protection: prevent removing the last Owner from a tenant.
    /// </summary>
    public bool ProtectLastOwner { get; set; } = true;

    /// <summary>
    /// Maximum number of Platform Admins allowed.
    /// </summary>
    public int MaxPlatformAdmins { get; set; } = 10;
}

/// <summary>
/// Represents a Platform Admin principal.
/// </summary>
public sealed record PlatformAdminPrincipal(
    Guid IdentityUserId,
    string Email,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastMfaChallengeUtc);

/// <summary>
/// Represents a four-eyes authorization request.
/// </summary>
public sealed record FourEyesAuthorization(
    Guid RequestId,
    Guid InitiatorIdentityUserId,
    string Action,
    string ResourceType,
    string ResourceId,
    string Reason,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    FourEyesStatus Status,
    Guid? ApproverIdentityUserId,
    DateTimeOffset? ApprovedAtUtc);

public enum FourEyesStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
    Cancelled = 5
}

/// <summary>
/// Platform Admin action types for audit.
/// </summary>
public static class PlatformAdminActions
{
    public const string TenantList = "tenant.list";
    public const string TenantGet = "tenant.get";
    public const string TenantCreate = "tenant.create";
    public const string TenantUpdate = "tenant.update";
    public const string TenantSuspend = "tenant.suspend";
    public const string TenantUnsuspend = "tenant.unsuspend";
    public const string TenantDelete = "tenant.delete";
    public const string TenantAdminList = "tenant.admin.list";
    public const string TenantAdminAdd = "tenant.admin.add";
    public const string TenantAdminRemove = "tenant.admin.remove";
    public const string PlatformAdminList = "platform_admin.list";
    public const string PlatformAdminCreate = "platform_admin.create";
    public const string PlatformAdminDelete = "platform_admin.delete";
    public const string GlobalConfigGet = "global.config.get";
    public const string GlobalConfigChange = "global.config.change";
    public const string FourEyesRequest = "four_eyes.request";
    public const string FourEyesApprove = "four_eyes.approve";
    public const string FourEyesReject = "four_eyes.reject";
    public const string AuditLogQuery = "audit.log.query";
}

/// <summary>
/// Interface for Platform Admin operations.
/// </summary>
public interface IPlatformAdminService
{
    /// <summary>
    /// Checks if an identity is a Platform Admin.
    /// </summary>
    Task<bool> IsPlatformAdminAsync(Guid identityUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Platform Admins.
    /// </summary>
    Task<IReadOnlyList<PlatformAdminPrincipal>> ListPlatformAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Platform Admin (requires four-eyes).
    /// </summary>
    Task<PlatformAdminPrincipal> CreatePlatformAdminAsync(
        Guid identityUserId,
        string email,
        string displayName,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a Platform Admin (requires four-eyes, protects last admin).
    /// </summary>
    Task<bool> RemovePlatformAdminAsync(
        Guid identityUserId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all tenants (cross-tenant).
    /// </summary>
    Task<IReadOnlyList<TenantSummary>> ListAllTenantsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tenant by ID (cross-tenant).
    /// </summary>
    Task<TenantSummary?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a tenant (requires four-eyes).
    /// </summary>
    Task<bool> SuspendTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsuspends a tenant (requires four-eyes).
    /// </summary>
    Task<bool> UnsuspendTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tenant (requires four-eyes, irreversible).
    /// </summary>
    Task<bool> DeleteTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests four-eyes authorization for a critical action.
    /// </summary>
    Task<FourEyesAuthorization> RequestFourEyesAsync(
        Guid initiatorIdentityUserId,
        string action,
        string resourceType,
        string resourceId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a four-eyes authorization request.
    /// </summary>
    Task<bool> ApproveFourEyesAsync(
        Guid requestId,
        Guid approverIdentityUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a four-eyes authorization request.
    /// </summary>
    Task<bool> RejectFourEyesAsync(
        Guid requestId,
        Guid approverIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Platform Admin audit log.
    /// </summary>
    Task<PlatformAdminAuditPage> QueryAuditLogAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? action = null,
        Guid? actorIdentityUserId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tenant summary for cross-tenant listing.
/// </summary>
public sealed record TenantSummary(
    Guid TenantId,
    string Name,
    string Subdomain,
    TenantStatus Status,
    int AdminCount,
    int ProfessionalCount,
    int CustomerCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Platform Admin audit log entry.
/// </summary>
public sealed record PlatformAdminAuditEntry(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    Guid ActorIdentityUserId,
    string ActorEmail,
    string Action,
    string ResourceType,
    string ResourceId,
    string? Reason,
    string? Metadata,
    bool Success,
    string? ErrorMessage);

/// <summary>
/// Paged Platform Admin audit log.
/// </summary>
public sealed record PlatformAdminAuditPage(
    IReadOnlyList<PlatformAdminAuditEntry> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// V103: Platform Admin service with cross-tenant administration,
/// four-eyes authorization, step-up MFA, and comprehensive audit.
/// Focuses on Platform Admin management and cross-tenant operations.
/// Tenant admin management is handled via tenant-scoped APIs.
/// </summary>
public sealed class PlatformAdminService : IPlatformAdminService
{
    private readonly ILogger<PlatformAdminService> _logger;
    private readonly PlatformAdminOptions _options;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public PlatformAdminService(
        ILogger<PlatformAdminService> logger,
        IOptions<PlatformAdminOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _dataProtectionProvider = dataProtectionProvider ?? throw new ArgumentNullException(nameof(dataProtectionProvider));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<bool> IsPlatformAdminAsync(Guid identityUserId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var admin = await dbContext.PlatformAdmins
            .FirstOrDefaultAsync(a => a.IdentityUserId == identityUserId && a.IsActive, cancellationToken);

        return admin is not null;
    }

    public async Task<IReadOnlyList<PlatformAdminPrincipal>> ListPlatformAdminsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return Array.Empty<PlatformAdminPrincipal>();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var admins = await dbContext.PlatformAdmins
            .Where(a => a.IsActive)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new PlatformAdminPrincipal(
                a.IdentityUserId,
                a.Email,
                a.DisplayName,
                a.IsActive,
                a.CreatedAtUtc,
                a.LastMfaChallengeUtc))
            .ToListAsync(cancellationToken);

        return admins;
    }

    public async Task<PlatformAdminPrincipal> CreatePlatformAdminAsync(
        Guid identityUserId,
        string email,
        string displayName,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Platform Admin is not enabled.");

        // Validate requester is a Platform Admin
        if (!await IsPlatformAdminAsync(requesterIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Requester is not a Platform Admin.");

        // Check four-eyes requirement
        if (_options.RequireFourEyesForCriticalActions)
        {
            var fourEyes = await RequestFourEyesAsync(
                requesterIdentityUserId,
                PlatformAdminActions.PlatformAdminCreate,
                "platform_admin",
                identityUserId.ToString(),
                reason,
                cancellationToken);

            if (fourEyes.Status != FourEyesStatus.Approved)
                throw new InvalidOperationException("Four-eyes authorization required and not yet approved.");
        }

        // Check max Platform Admins
        var currentCount = (await ListPlatformAdminsAsync(cancellationToken)).Count;
        if (currentCount >= _options.MaxPlatformAdmins)
            throw new InvalidOperationException($"Maximum number of Platform Admins ({_options.MaxPlatformAdmins}) reached.");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        // Verify identity user exists
        var identityUser = await dbContext.Users.FindAsync(new object[] { identityUserId }, cancellationToken);
        if (identityUser == null)
            throw new ArgumentException("Identity user not found.", nameof(identityUserId));

        // Check if already a Platform Admin
        var existing = await dbContext.PlatformAdmins
            .FirstOrDefaultAsync(a => a.IdentityUserId == identityUserId, cancellationToken);
        if (existing != null)
        {
            if (existing.IsActive)
                throw new InvalidOperationException("Identity user is already a Platform Admin.");
            // Reactivate
            existing.IsActive = true;
            existing.DisplayName = displayName;
            existing.Email = email;
        }
        else
        {
            var admin = new PlatformAdminEntity
            {
                IdentityUserId = identityUserId,
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByIdentityUserId = requesterIdentityUserId
            };
            dbContext.PlatformAdmins.Add(admin);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(requesterIdentityUserId, PlatformAdminActions.PlatformAdminCreate, "platform_admin", identityUserId.ToString(), true, reason, cancellationToken);

        return new PlatformAdminPrincipal(identityUserId, email, displayName, true, DateTimeOffset.UtcNow, null);
    }

    public async Task<bool> RemovePlatformAdminAsync(
        Guid identityUserId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        // Validate requester is a Platform Admin
        if (!await IsPlatformAdminAsync(requesterIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Requester is not a Platform Admin.");

        // Check four-eyes requirement
        if (_options.RequireFourEyesForCriticalActions)
        {
            var fourEyes = await RequestFourEyesAsync(
                requesterIdentityUserId,
                PlatformAdminActions.PlatformAdminDelete,
                "platform_admin",
                identityUserId.ToString(),
                reason,
                cancellationToken);

            if (fourEyes.Status != FourEyesStatus.Approved)
                throw new InvalidOperationException("Four-eyes authorization required and not yet approved.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var admin = await dbContext.PlatformAdmins
            .FirstOrDefaultAsync(a => a.IdentityUserId == identityUserId && a.IsActive, cancellationToken);
        if (admin == null) return false;

        // Protect last Platform Admin
        var activeCount = await dbContext.PlatformAdmins.CountAsync(a => a.IsActive, cancellationToken);
        if (activeCount <= 1)
            throw new InvalidOperationException("Cannot remove the last Platform Admin.");

        admin.IsActive = false;
        admin.RemovedAtUtc = DateTimeOffset.UtcNow;
        admin.RemovedByIdentityUserId = requesterIdentityUserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(requesterIdentityUserId, PlatformAdminActions.PlatformAdminDelete, "platform_admin", identityUserId.ToString(), true, reason, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<TenantSummary>> ListAllTenantsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return Array.Empty<TenantSummary>();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        // Query the tenant profiles from the identity schema
        var tenants = await dbContext.TenantProfiles
            .OrderBy(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TenantSummary(
                t.TenantId,
                t.Name,
                t.Subdomain,
                t.Status,
                t.AdminCount,
                t.ProfessionalCount,
                t.CustomerCount,
                t.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return tenants;
    }

    public async Task<TenantSummary?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var tenant = await dbContext.TenantProfiles
            .Where(t => t.TenantId == tenantId)
            .Select(t => new TenantSummary(
                t.TenantId,
                t.Name,
                t.Subdomain,
                t.Status,
                t.AdminCount,
                t.ProfessionalCount,
                t.CustomerCount,
                t.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return tenant;
    }

    public async Task<bool> SuspendTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        // Validate requester is a Platform Admin
        if (!await IsPlatformAdminAsync(requesterIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Requester is not a Platform Admin.");

        // Check four-eyes requirement
        if (_options.RequireFourEyesForCriticalActions)
        {
            var fourEyes = await RequestFourEyesAsync(
                requesterIdentityUserId,
                PlatformAdminActions.TenantSuspend,
                "tenant",
                tenantId.ToString(),
                reason,
                cancellationToken);

            if (fourEyes.Status != FourEyesStatus.Approved)
                throw new InvalidOperationException("Four-eyes authorization required and not yet approved.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var tenant = await dbContext.TenantProfiles
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (tenant == null) return false;

        tenant.Status = TenantStatus.Suspended;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(requesterIdentityUserId, PlatformAdminActions.TenantSuspend, "tenant", tenantId.ToString(), true, reason, cancellationToken);

        return true;
    }

    public async Task<bool> UnsuspendTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        if (!await IsPlatformAdminAsync(requesterIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Requester is not a Platform Admin.");

        if (_options.RequireFourEyesForCriticalActions)
        {
            var fourEyes = await RequestFourEyesAsync(
                requesterIdentityUserId,
                PlatformAdminActions.TenantUnsuspend,
                "tenant",
                tenantId.ToString(),
                reason,
                cancellationToken);

            if (fourEyes.Status != FourEyesStatus.Approved)
                throw new InvalidOperationException("Four-eyes authorization required and not yet approved.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var tenant = await dbContext.TenantProfiles
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (tenant == null) return false;

        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(requesterIdentityUserId, PlatformAdminActions.TenantUnsuspend, "tenant", tenantId.ToString(), true, reason, cancellationToken);

        return true;
    }

    public async Task<bool> DeleteTenantAsync(
        Guid tenantId,
        Guid requesterIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        if (!await IsPlatformAdminAsync(requesterIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Requester is not a Platform Admin.");

        if (_options.RequireFourEyesForCriticalActions)
        {
            var fourEyes = await RequestFourEyesAsync(
                requesterIdentityUserId,
                PlatformAdminActions.TenantDelete,
                "tenant",
                tenantId.ToString(),
                reason,
                cancellationToken);

            if (fourEyes.Status != FourEyesStatus.Approved)
                throw new InvalidOperationException("Four-eyes authorization required and not yet approved.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var tenant = await dbContext.TenantProfiles
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (tenant == null) return false;

        // Soft delete - mark as deleted
        tenant.Status = TenantStatus.Closed;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(requesterIdentityUserId, PlatformAdminActions.TenantDelete, "tenant", tenantId.ToString(), true, reason, cancellationToken);

        return true;
    }

    public async Task<FourEyesAuthorization> RequestFourEyesAsync(
        Guid initiatorIdentityUserId,
        string action,
        string resourceType,
        string resourceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Platform Admin is not enabled.");

        var requestId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(24); // 24 hours to approve

        var fourEyes = new FourEyesAuthorization(
            requestId,
            initiatorIdentityUserId,
            action,
            resourceType,
            resourceId,
            reason,
            now,
            expiresAt,
            FourEyesStatus.Pending,
            null,
            null);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        dbContext.FourEyesAuthorizations.Add(new FourEyesAuthorizationEntity
        {
            RequestId = requestId,
            InitiatorIdentityUserId = initiatorIdentityUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Reason = reason,
            RequestedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            Status = FourEyesStatus.Pending
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(initiatorIdentityUserId, PlatformAdminActions.FourEyesRequest, resourceType, resourceId, true, reason, cancellationToken);

        return fourEyes;
    }

    public async Task<bool> ApproveFourEyesAsync(
        Guid requestId,
        Guid approverIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        // Approver must be a Platform Admin (and not the initiator)
        if (!await IsPlatformAdminAsync(approverIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Approver is not a Platform Admin.");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var fourEyes = await dbContext.FourEyesAuthorizations
            .FirstOrDefaultAsync(f => f.RequestId == requestId, cancellationToken);
        if (fourEyes == null) return false;
        if (fourEyes.Status != FourEyesStatus.Pending) return false;
        if (fourEyes.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            fourEyes.Status = FourEyesStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }
        if (fourEyes.InitiatorIdentityUserId == approverIdentityUserId)
            throw new InvalidOperationException("Initiator cannot approve their own request.");

        fourEyes.Status = FourEyesStatus.Approved;
        fourEyes.ApproverIdentityUserId = approverIdentityUserId;
        fourEyes.ApprovedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(approverIdentityUserId, PlatformAdminActions.FourEyesApprove, fourEyes.ResourceType, fourEyes.ResourceId, true, $"Approved four-eyes for {fourEyes.Action}", cancellationToken);

        return true;
    }

    public async Task<bool> RejectFourEyesAsync(
        Guid requestId,
        Guid approverIdentityUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return false;

        if (!await IsPlatformAdminAsync(approverIdentityUserId, cancellationToken))
            throw new UnauthorizedAccessException("Approver is not a Platform Admin.");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var fourEyes = await dbContext.FourEyesAuthorizations
            .FirstOrDefaultAsync(f => f.RequestId == requestId, cancellationToken);
        if (fourEyes == null) return false;
        if (fourEyes.Status != FourEyesStatus.Pending) return false;

        fourEyes.Status = FourEyesStatus.Rejected;
        fourEyes.ApproverIdentityUserId = approverIdentityUserId;
        fourEyes.ApprovedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(approverIdentityUserId, PlatformAdminActions.FourEyesReject, fourEyes.ResourceType, fourEyes.ResourceId, true, $"Rejected four-eyes for {fourEyes.Action}: {reason}", cancellationToken);

        return true;
    }

    public async Task<PlatformAdminAuditPage> QueryAuditLogAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? action = null,
        Guid? actorIdentityUserId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return new PlatformAdminAuditPage(Array.Empty<PlatformAdminAuditEntry>(), page, pageSize, 0);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var query = dbContext.PlatformAdminAuditLogs.AsQueryable();

        query = query.Where(a => a.OccurredAtUtc >= fromUtc && a.OccurredAtUtc <= toUtc);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (actorIdentityUserId.HasValue)
            query = query.Where(a => a.ActorIdentityUserId == actorIdentityUserId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new PlatformAdminAuditEntry(
                a.AuditId,
                a.OccurredAtUtc,
                a.ActorIdentityUserId,
                a.ActorEmail,
                a.Action,
                a.ResourceType,
                a.ResourceId,
                a.Reason,
                a.Metadata,
                a.Success,
                a.ErrorMessage))
            .ToListAsync(cancellationToken);

        return new PlatformAdminAuditPage(items, page, pageSize, totalCount);
    }

    private async Task AuditAsync(
        Guid actorIdentityUserId,
        string action,
        string resourceType,
        string resourceId,
        bool success,
        string? reason,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtendeeIdentityDbContext>();

        var identityUser = await dbContext.Users
            .Where(u => u.Id == actorIdentityUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        dbContext.PlatformAdminAuditLogs.Add(new PlatformAdminAuditLogEntity
        {
            AuditId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ActorIdentityUserId = actorIdentityUserId,
            ActorEmail = identityUser ?? "unknown",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Reason = reason,
            Success = success
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

// Entity classes for Platform Admin
public sealed class TenantProfileEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public int AdminCount { get; set; }
    public int ProfessionalCount { get; set; }
    public int CustomerCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PlatformAdminEntity
{
    public Guid IdentityUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastMfaChallengeUtc { get; set; }
    public Guid CreatedByIdentityUserId { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public Guid? RemovedByIdentityUserId { get; set; }
}

public sealed class FourEyesAuthorizationEntity
{
    public Guid RequestId { get; set; }
    public Guid InitiatorIdentityUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public FourEyesStatus Status { get; set; }
    public Guid? ApproverIdentityUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}

public sealed class PlatformAdminAuditLogEntity
{
    public Guid AuditId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid ActorIdentityUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Metadata { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

