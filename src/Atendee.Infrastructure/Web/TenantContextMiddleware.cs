using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Atendee.Application.Abstractions;
using Atendee.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Atendee.Infrastructure.Web;

/// <summary>
/// Resolves every authenticated operational request against the current
/// database membership. No tenant ID from headers, query strings, route values
/// or request bodies is consulted.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    private const string TenantIdClaim = "atendee:tenant_id";
    private const string TenantRoleClaim = "atendee:tenant_role";
    private const string TenantStatusClaim = "atendee:tenant_status";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantMembershipResolver resolver,
        TrustedRequestContext trustedContext,
        Microsoft.AspNetCore.Identity.UserManager<OperationalIdentityUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var identityUserIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(identityUserIdValue, out var identityUserId) || identityUserId == Guid.Empty)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized);
            return;
        }

        var membership = await resolver.ResolveAsync(identityUserId, context.RequestAborted);
        if (membership is null || !membership.IsActive)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized);
            return;
        }

        // Tenant lifecycle policy is evaluated on every request, so a status or
        // role change takes effect on the member's next request.
        if (!Atendee.Domain.Tenants.TenantAccessPolicy.IsAllowed(
                membership.TenantStatus,
                membership.Role,
                Atendee.Domain.Tenants.TenantCapability.Login))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden);
            return;
        }

        // A session created as a lower-privilege salon user must not inherit
        // Owner/Manager access after a server-side role promotion. Enforce
        // TOTP and the signed amr=mfa marker on every request.
        if (membership.Role is Atendee.Domain.Tenants.TenantRole.Owner or
            Atendee.Domain.Tenants.TenantRole.Manager)
        {
            var operationalUser = await userManager.FindByIdAsync(identityUserId.ToString());
            var hasMfaAuthentication = context.User.FindAll("amr")
                .Any(claim => claim.Value == "mfa");

            if (operationalUser is null ||
                !operationalUser.TwoFactorEnabled ||
                !hasMfaAuthentication)
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden);
                return;
            }
        }

        trustedContext.Establish(membership);

        // Replace any stale membership claims with values resolved from the
        // server-side membership record on this request.
        if (context.User.Identity is ClaimsIdentity claimsIdentity)
        {
            foreach (var claimType in new[] { TenantIdClaim, TenantRoleClaim, TenantStatusClaim })
            {
                foreach (var claim in claimsIdentity.FindAll(claimType).ToArray())
                    claimsIdentity.RemoveClaim(claim);
            }

            claimsIdentity.AddClaim(new Claim(TenantIdClaim, membership.TenantId.ToString()));
            claimsIdentity.AddClaim(new Claim(TenantRoleClaim, membership.Role.ToString()));
            claimsIdentity.AddClaim(new Claim(TenantStatusClaim, membership.TenantStatus.ToString()));
        }

        await next(context);
    }

    private static async Task RejectAsync(HttpContext context, int statusCode)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.Response.StatusCode = statusCode;
    }
}
