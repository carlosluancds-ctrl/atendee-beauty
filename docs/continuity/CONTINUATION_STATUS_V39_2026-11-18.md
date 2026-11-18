# Atendee Beauty - Continuity Status V39 (2026-11-18)

## Scope
Trusted tenant association for identity-target audit events. No audit read view, query/export endpoint, Platform Admin functionality, or new HTTP endpoint is introduced.

## Verified starting state
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `security/csrf-webhook-antiforgery-v23`
- Starting HEAD: `7af533693e34648663fd8476e063a9a14080579e`
- Starting HEAD matches the remote branch SHA.
- Starting worktree was clean.
- V38 content commit `405b985dcb8054d25e9c37f2ea0eb4961d6bcad6` passed CI run [44225192164](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44225192164) for that exact SHA.
- V38 evidence/index follow-up commit `7af533693e34648663fd8476e063a9a14080579e` passed CI run [44225499047](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44225499047) for that exact SHA.
- `main` remains untouched. No deploy, public signup, or administrative endpoint was created.

## V39 implementation
- Added migration `db/migrations/009_resolve_audit_target_tenant.sql`, applied after migrations 001-008 in the PostgreSQL 18 workflow.
- Added narrow `atendee.resolve_audit_target_tenant()`, owned by `atendee_owner`, which returns only the tenant ID associated with the transaction-local `app.identity_user_id`.
- Revoked PUBLIC execution and granted execution only to `atendee_identity_owner`. The auth-login role does not receive direct SELECT access to `atendee.tenant_users` and cannot execute this resolver.
- Added `identity.populate_security_audit_target_tenant()` as a SECURITY DEFINER BEFORE INSERT trigger on `identity."SecurityAuditEvents"`.
- For identity-target events, the trigger temporarily sets `app.identity_user_id` to the row's target identity, resolves the persisted membership tenant, restores the previous setting, and overwrites any supplied `TargetTenantId`.
- If no persisted membership exists, `TargetTenantId` remains NULL. Tenant-status events are unchanged because they have no identity target.
- Relaxed the target-kind check only enough to permit a nullable tenant target alongside an identity target; tenant-status events retain their tenant-only/from-to status requirements.
- No FK to `atendee.tenants` was added because tenant deletion and post-Closed retention remain product decisions. The resolver guarantees the tenant ID is backed by persisted membership at event insertion time.
- Historical audit events are intentionally not backfilled: current membership is not proof of the tenant at the time of an older event. Only new inserts are populated.
- Updated `.github/workflows/postgres18-persistence.yml` to apply migration 009.

## Tests added/extended
- Membership role-change audit assertion now verifies the exact persisted `TargetTenantId` as well as the trusted actor.
- A direct insert attempts to supply a forged tenant ID; the database trigger overwrites it with the target identity's persisted tenant.
- Verifies the auth-login role cannot directly SELECT tenant memberships or execute the narrow resolver.
- Verifies all six tested identity-target audit events for an identity with membership carry the exact persisted tenant ID.
- Verifies an identity without membership gets an audit event with `TargetTenantId = NULL`.
- Existing client-forged actor test remains in place.

## Local validation
- `git diff --check`: passed.
- `dotnet build Atendee.sln --no-restore --configuration Release -v:q`: succeeded, 0 warnings and 0 errors.
- `Atendee.Domain.Checks`: 19/19 passed.
- PostgreSQL 18 integration checks require the CI disposable database and will be recorded after the exact V39 commit is pushed and CI completes.

## Product/security boundaries
- Do not expose tenant-visible audit read views or query/export endpoints until the owner decides audit visibility, retention, and post-Closed handling.
- Do not add Platform Admin or tenant-status mutation functionality until the remaining owner decisions are resolved.
- Do not infer a tenant for identities without persisted membership.
- Do not change the approved tenant-status access matrix.
- No changes to `main`, no deploy, no public signup, no admin endpoints.

## Next step
After V39 CI succeeds, ask the owner to resolve the remaining product decisions before any audit read/export work. In particular: tenant-visible audit scope, retention and post-Closed behavior. Review the proposed last-Owner database invariant with the owner before treating it as an approved business rule.

## Completion state
V39 code and this status file must be committed and pushed on the work branch. The step remains pending until GitHub Actions completes successfully for the exact commit SHA and the worktree/remote branch are verified clean and synchronized.
