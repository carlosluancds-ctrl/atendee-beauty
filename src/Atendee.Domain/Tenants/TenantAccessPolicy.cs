
namespace Atendee.Domain.Tenants;

/// <summary>
/// Single source of truth for what a tenant member may do in each tenant
/// lifecycle status. Deny by default: anything not listed, any undefined
/// status or role, and any composite or empty capability is refused.
/// Decision record: docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md.
/// Evaluated on every authenticated request, so a status change takes effect
/// on the member's next request.
/// </summary>
public static class TenantAccessPolicy
{
    private const TenantCapability FullOperation =
        TenantCapability.Login |
        TenantCapability.ReadData |
        TenantCapability.WriteData |
        TenantCapability.Automation |
        TenantCapability.ManageTeamAndSettings |
        TenantCapability.Billing |
        TenantCapability.ExportData;

    public static TenantCapability CapabilitiesFor(TenantStatus status, TenantRole role)
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(role))
            return TenantCapability.None;

        return status switch
        {
            // Only the owner completes verification; no business data exists yet.
            TenantStatus.PendingVerification => role == TenantRole.Owner
                ? TenantCapability.Login | TenantCapability.CompleteOnboarding
                : TenantCapability.None,

            // Owner and Manager configure the salon; no automation or export until setup is done.
            TenantStatus.SetupRequired => role is TenantRole.Owner or TenantRole.Manager
                ? TenantCapability.Login |
                  TenantCapability.CompleteOnboarding |
                  TenantCapability.ReadData |
                  TenantCapability.WriteData |
                  TenantCapability.ManageTeamAndSettings |
                  TenantCapability.Billing
                : TenantCapability.None,

            // PastDue is a grace state: full operation continues. The grace length is a
            // commercial parameter enforced by the billing process that moves the tenant
            // to Suspended, not by this policy.
            TenantStatus.Trial or TenantStatus.Active or TenantStatus.PastDue => FullOperation,

            // Read-only, pay and export. Matches ESPEC_IDENTIDADE_LOGIN_CADASTRO_V1 section 3, step 9.
            TenantStatus.Suspended =>
                TenantCapability.Login |
                TenantCapability.ReadData |
                TenantCapability.Billing |
                TenantCapability.ExportData,

            // Wind-down: only the owner, to review, settle and export their own data.
            TenantStatus.Closing => role == TenantRole.Owner
                ? TenantCapability.Login |
                  TenantCapability.ReadData |
                  TenantCapability.Billing |
                  TenantCapability.ExportData
                : TenantCapability.None,

            TenantStatus.Closed => TenantCapability.None,

            _ => TenantCapability.None
        };
    }

    /// <summary>
    /// True only when exactly one defined capability is requested and the status
    /// and role grant it. Without Login, no other capability is ever granted.
    /// </summary>
    public static bool IsAllowed(TenantStatus status, TenantRole role, TenantCapability capability)
    {
        var requested = (int)capability;
        var single = requested != 0 && (requested & (requested - 1)) == 0;
        if (!single || (capability & ~FullOperationAndOnboarding) != 0)
            return false;

        var granted = CapabilitiesFor(status, role);
        if (!granted.HasFlag(TenantCapability.Login))
            return false;

        return granted.HasFlag(capability);
    }

    private const TenantCapability FullOperationAndOnboarding =
        FullOperation | TenantCapability.CompleteOnboarding;
}
