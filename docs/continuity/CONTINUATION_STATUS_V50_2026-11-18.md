# Atendee Beauty — Continuity Status V50 (2026-11-18)

## Scope
Backend-first implementation of the salon's first operational onboarding transition.

## Implemented in this commit
- Added GET /api/v1/tenant. It returns only the salon profile already resolved by trusted server-side membership middleware; no tenant ID is accepted from the client.
- Added POST /api/v1/tenant/onboarding/complete.
- Completion requires the current trusted member to be the active Owner, tenant lifecycle policy to grant CompleteOnboarding, a signed session carrying amr=mfa, and an ASP.NET Identity user with confirmed email and MFA enabled.
- Added migration 012 with a SECURITY DEFINER PostgreSQL function owned by atendee_owner. It verifies active Owner membership, locks the tenant row, moves only PendingVerification (0) to SetupRequired (1), and returns SetupRequired unchanged for safe retries.
- The function is executable by atendee_app_login only; the application login receives schema USAGE but no direct table privileges. Existing migration 007 trigger writes tenant_status.changed atomically with the state update.
- Extended HTTP integration checks to provision an MFA Owner in PendingVerification, read the profile, complete onboarding, verify the status audit, and repeat completion without a duplicate audit event.
- No frontend changes, commercial trial activation, legal acceptance, DNS, SES, production data or deployment.

## Validation status
- Local full solution build was not possible from the temporary C:\Temp\atendee-work snapshot: it predates the current branch's API service/professional and tenant-profile files. The branch itself is the source of truth; CI is required to validate the commit.
- No local PostgreSQL 18.7 instance is configured in this temporary workspace.
- GitHub Actions will run on this commit. Record its actual result in a subsequent continuity checkpoint; do not infer success from dispatch.

## Security boundaries
- Tenant and actor identifiers are derived from trusted middleware state, never request body, query, header or route values.
- Owner MFA is enforced by existing middleware and rechecked through signed amr=mfa plus persisted TwoFactorEnabled; email confirmation is checked through UserManager.
- PostgreSQL function verifies active Owner membership and performs the status mutation under row lock.
- Database audit trigger is the source of truth for the lifecycle change.
- Repeated completion is idempotent only while tenant remains SetupRequired; any later lifecycle state returns no row and is forbidden.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23. Main remains untouched.
- Parent: 3847783719b37fabe142ca87601a66e95277be52.

## Next
1. Wait for the full CI run and fix any build, migration or integration failure.
2. Add a manager/receptionist negative authorization test if the initial integration pass exposes a gap.
3. After backend CI passes, build the salon onboarding UI using the approved business workflow; do not invent trial or legal requirements.
