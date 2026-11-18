
namespace Atendee.Domain.Tenants;

/// <summary>
/// Tenant-level capability families gated by the tenant lifecycle status.
/// These are coarse gates evaluated BEFORE the fine-grained role permissions
/// (see docs/product/MATRIZ_PERMISSOES_V1.txt). A capability being allowed by
/// the status never grants a permission the role does not already hold.
/// </summary>
[Flags]
public enum TenantCapability
{
    None = 0,

    /// <summary>Authenticate and keep an operational session.</summary>
    Login = 1 << 0,

    /// <summary>Verify contact, accept terms and complete the essential salon data.</summary>
    CompleteOnboarding = 1 << 1,

    /// <summary>Read tenant business data (customers, agenda, services, reports).</summary>
    ReadData = 1 << 2,

    /// <summary>Create or change tenant business data.</summary>
    WriteData = 1 << 3,

    /// <summary>AI replies and outbound messaging that incur platform or provider costs.</summary>
    Automation = 1 << 4,

    /// <summary>Team, roles, tenant settings, AI configuration and integrations.</summary>
    ManageTeamAndSettings = 1 << 5,

    /// <summary>View subscription status and update payment.</summary>
    Billing = 1 << 6,

    /// <summary>Export the tenant's own data.</summary>
    ExportData = 1 << 7
}
