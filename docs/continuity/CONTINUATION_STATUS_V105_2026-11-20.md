# Continuation status V105 - V104 HTTP integration tests for tenant-scoped administrative member endpoints

Date: 2026-11-20
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- V104 HTTP integration tests added to `tests/Atendee.Api.HttpChecks/Program.cs`
- Local validation: `git diff --check` clean and Release build of Atendee.Api and Atendee.Api.HttpChecks with 0 warnings and 0 errors.

## Delivered

1. **V104 HTTP integration tests** covering all four tenant-scoped administrative member endpoints:
   - `GET /api/v1/tenant/members/{identityUserId}` — get member info (any active member of the tenant)
   - `PUT /api/v1/tenant/members/{identityUserId}/role` — change member role (A4)
   - `POST /api/v1/tenant/members/{identityUserId}/deactivate` — deactivate membership (A1 variant)
   - `POST /api/v1/tenant/members/{identityUserId}/reactivate` — reactivate membership (A5)

2. **Authorization test coverage** per ADMIN_ACTIONS_DESIGN_V1.md section 8:
   - Owner authorization: any non-Owner role (role change, deactivate, reactivate)
   - Manager authorization: Receptionist/Professional only
   - Self-target protection: cannot change own role, cannot deactivate/reactivate self
   - Cross-tenant isolation: returns 404 (not 403)
   - Last-Owner protection: cannot deactivate/change role of an Owner
   - Unauthenticated requests: return 401
   - Missing antiforgery token: returns 400
   - Invalid GUID format: returns 400
   - Inactive target: returns 404

3. **Transactional audit verification**:
   - Role change: `tenant_membership.role_changed` audited exactly once
   - Deactivation: `tenant_membership.deactivated` audited exactly once
   - Reactivation: `tenant_membership.activated` audited exactly once

4. **Rate limiting verification**: endpoints protected by `authenticated-api` policy (120 req/min per authenticated user)

5. **No migration, schema change, provider, credential, production configuration or ordering change**. Authentication-before-antiforgery, tenant isolation, RLS, mandatory audit, transaction runner and MFA/TOTP protections are untouched.

## Test structure

Tests follow existing patterns in `Atendee.Api.HttpChecks`:
- Uses isolated `mfaFactory` WebApplicationFactory for MFA-enabled test clients
- Creates test identities via `UserManager<OperationalIdentityUser>` 
- Seeds memberships directly via SQL using `SeedMembershipAsync` / `SeedAdditionalMembershipAsync`
- Verifies audit events via `ReadSecurityAuditTypesAsync` against the database
- Uses `UpdateMembershipAsync` helper for direct database state manipulation
- Tests both Owner and Manager clients with MFA enrollment completed
- Tests self-target protection with Receptionist client
- Tests cross-tenant isolation with separate tenant

## Limits and notes

- Member listing endpoint returns 501 (requires dedicated query endpoint); individual lookup works.
- Step-up MFA freshness window and four-eyes for cross-tenant actions remain open decisions per ADMIN_ACTIONS_DESIGN_V1.md (items 3, 10).
- Platform Admin (V103) and A1/A2 cross-tenant administration remain opt-in and blocked pending explicit approval.
- The existing `DisableAsync` and `RevokeSessionsAsync` operate on the global identity (lockout + security stamp), while the new membership operations are tenant-scoped soft state changes.
- TestServer runs in-process so rate limit window exhaustion is not easily tested; instead verifies rate limit headers are present.

## Still blocked or requiring decisions

Unchanged from V104: appointment rescheduling/cancellation policy; outbox dispatcher and handlers with provider/credential/cost approvals; Data Protection and limiter HMAC coordinated key rotation; Platform Admin and A1/A2 actions; WhatsApp Cloud API (Meta Business approval, provider, credentials, costs); independent security review, backup/restore validation, trusted proxy configuration, orchestrator probe wiring to /health/live and /health/ready, and deploy decisions. CI success is not production authorization.

## Continuation

Read CLAUDE.md, docs/continuity/CONTINUE_HERE.md, this status, docs/continuity/manifest.json and docs/technical/ADMIN_ACTIONS_DESIGN_V1.md. Active worktree: C:\Users\iaras\Downloads\atendee-beauty-v85-work. Preserve C:\Users\iaras\Downloads\atendee-beauty-git untouched.