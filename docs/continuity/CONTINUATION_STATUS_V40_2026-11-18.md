# Atendee Beauty - Continuity Status V40 (2026-11-18)

## Scope
Enforce the approved last-Owner/MFA business invariant at the PostgreSQL boundary and support first-Owner MFA enrollment before the identity receives its first salon membership. No public signup, tenant creation endpoint, admin endpoint, audit read/export endpoint, or deployment was introduced.

## Owner decision recorded
On 2026-11-18, the owner explicitly approved:
- Every persisted salon must have at least one active Owner with MFA enabled.
- No active Owner may lack MFA, even during initial onboarding.
- The first Owner's identity must complete MFA enrollment before an active Owner membership is created.

This is stricter than merely requiring one MFA-enabled Owner while permitting other active Owners without MFA. The database now enforces both parts.

## Verified starting state
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23
- Code commit tested by CI: fe01831eb23cab5552c630372e4ac7bc6742f34f
- Local HEAD and remote branch SHA were verified equal to that exact commit using git ls-remote.
- Worktree was clean after the code push.
- main was not changed.
- No deployment was performed.

## V40 implementation
- Added migration db/migrations/010_enforce_active_mfa_owner.sql and added it to .github/workflows/postgres18-persistence.yml after migration 009.
- Added a narrow SELECT policy and grant for the NOLOGIN atendee_identity_owner role on atendee.tenant_users. This is used by SECURITY DEFINER invariant functions; the auth-login role still has no direct membership SELECT permission.
- Added identity.lock_tenant_owner_invariant(), which obtains a tenant-scoped PostgreSQL transaction advisory lock before relevant tenant, membership, and MFA mutations.
- Added deferred constraint validation through identity.assert_tenant_has_active_mfa_owner().
- The deferred check rejects:
  - a tenant committed without any active Owner with MFA enabled;
  - any active Owner whose identity has MFA disabled;
  - demotion, deactivation, or removal that would leave no active MFA-enabled Owner;
  - disabling MFA for an active Owner when it would violate the invariant.
- Triggers cover tenant insertion, tenant membership insert/update/delete, and changes to AspNetUsers.TwoFactorEnabled. The deferred check allows a tenant and its MFA-ready Owner membership to be inserted in the same transaction.
- Tenant-scoped advisory transaction locks serialize concurrent membership and MFA changes, preventing two simultaneous operations from both removing the final protected Owner.
- The API now permits a provisioned identity with no salon membership to complete password-verified MFA enrollment through the existing short-lived MFA-setup session. Enrollment endpoints allow this pre-membership state.
- Successful pre-membership enrollment returns mfa_enrolled_membership_pending with the recovery codes, clears the setup cookie, and deliberately does not issue an application session. Trusted provisioning can attach the identity as Owner only after TwoFactorEnabled is true.
- Existing Owner/Manager MFA behavior remains in place for identities that already have membership. No public registration, signup, or salon-provisioning HTTP endpoint was added.

## Tests added/extended
- Identity checks reject disabling MFA, demoting, deactivating, or deleting the last active Owner.
- Identity checks reject committing a tenant with no active MFA-enabled Owner.
- Identity checks permit demotion and MFA disablement for a former Owner when another protected Owner remains.
- Concurrent Owner demotion and deactivation checks verify exactly one active MFA-enabled Owner remains.
- API HTTP checks verify a provisioned identity can enroll MFA before membership, that no application session is issued before membership, and that the identity must complete a fresh MFA challenge after trusted membership provisioning.
- API HTTP checks verify promotion to Owner is blocked until MFA is enabled and that an existing password-only session remains denied after server-side Owner promotion.
- Existing Manager enrollment and authentication/session tests continue to run.

## Local validation
- git diff --check: passed.
- dotnet build Atendee.sln --no-restore --configuration Release -v:q: succeeded, 0 warnings and 0 errors.
- Domain checks: 19/19 passed locally.
- CI run 44231852468 completed success for the exact code SHA fe01831eb23cab5552c630372e4ac7bc6742f34f.
- CI used PostgreSQL 18.7 and passed:
  - Release build and domain checks: 19/19;
  - EF Core/PostgreSQL RLS persistence integration checks: 10/10;
  - Identity/TOTP/tenant-context checks: 31/31;
  - API HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix, and tenant-status checks.
- CI log explicitly confirms the negative Owner/MFA checks, concurrent serialization check, pre-membership MFA enrollment, and blocked Owner promotion.

## Product/security boundaries
- The pre-membership enrollment path accepts only an already-provisioned identity; it does not create accounts or salons.
- Trusted provisioning of the first Owner remains an internal/out-of-band operation. Do not add public signup or a provisioning/admin endpoint without a separate owner decision.
- Do not expose tenant-visible audit read views or query/export endpoints until the owner decides audit visibility, retention, and post-Closed handling.
- Do not add Platform Admin or tenant-status mutation functionality until the remaining owner decisions are resolved.
- Do not change the approved tenant-status access matrix.
- No changes to main, no deploy, no public signup, no admin endpoints.

## Next step
The Owner/MFA invariant is implemented and CI-verified. Before audit read/export work, ask the owner to decide tenant-visible audit scope, retention, and post-Closed handling. Any future first-Owner provisioning endpoint requires a separate explicit design/approval and must preserve the pre-enrollment-before-membership sequence.

## Completion state
V40 code commit fe01831eb23cab5552c6303724eac7bc6742f34f was pushed to security/csrf-webhook-antiforgery-v23 and passed PostgreSQL 18.7 CI run 44231852468 for that exact SHA. Local and remote branch SHAs matched, and the code worktree was clean. This status file and the updated index are the required versioned continuity artifacts for V40.
