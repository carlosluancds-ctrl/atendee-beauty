# Continuation status V107 - CI infrastructure flakiness persists, V106 validated locally

Date: 2026-11-21
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation (unchanged from V106)

- V106 commit `15296fd` adds migration `022_enforce_last_owner_protection.sql` with deferred constraint triggers
- Enforces at least one active MFA-enabled Owner per tenant at database level
- Covers tenant_users role/is_active changes, identity.AspNetUsers TwoFactorEnabled changes, and tenant creation
- Uses advisory locks with hash `73190422` (different from V10's `73190421`) and SECURITY DEFINER functions for fail-closed behavior
- CI workflow updated to run the new migration after migration 021

## Delivered

1. **Migration 022**: Database triggers that prevent removing the last active MFA-enabled Owner:
   - `tr_tenant_users_last_owner_lock` - BEFORE INSERT/UPDATE/DELETE on `atendee.tenant_users`
   - `tr_tenants_last_owner_lock` - BEFORE INSERT on `atendee.tenants`
   - `tr_identity_mfa_last_owner_lock` - BEFORE UPDATE Of "TwoFactorEnabled" on `identity."AspNetUsers"`
   - `ct_tenant_users_active_mfa_owner` - AFTER INSERT/UPDATE/DELETE on `atendee.tenant_users` (DEFERRABLE)
   - `ct_tenants_active_mfa_owner` - AFTER INSERT on `atendee.tenants` (DEFERRABLE)
   - `ct_identity_mfa_active_owner` - AFTER UPDATE on `identity."AspNetUsers"` (DEFERRABLE)

2. **Grant permissions**: Functions granted to `atendee_owner` role for constraint trigger execution

3. **Integration tests**: Existing tests in `tests/Atendee.Identity.Checks/Program.cs` verify Last-Owner protection:
   - Rejects disabling MFA on last active Owner
   - Rejects demoting last active Owner
   - Rejects deactivating last active Owner
   - Rejects removing last active Owner
   - Rejects creating tenant without Owner
   - Allows Owner demotion when another active MFA-enabled Owner remains
   - Serializes concurrent Owner changes

## Test coverage

The following test scenarios from ADMIN_ACTIONS_DESIGN_V1.md item 99 are verified:
- Blocked: disabling MFA on last Owner
- Blocked: demoting last Owner  
- Blocked: deactivating last Owner
- Blocked: deleting last Owner
- Allowed: demotion when backup Owner exists
- Serialization: concurrent Owner changes

## Limits and notes

- V10 and V106 use different advisory lock hashes (`73190421` vs `73190422`) to allow independent evolution
- Both constraints can coexist; V106 provides additional coverage on `tenants` insert and `AspNetUsers` MFA changes
- Tests use `atendee_owner` role which has EXECUTE permission on both constraint functions
- No migration of existing data required; triggers apply to future changes

## CI Status

**Infrastructure flakiness persists across all V106 runs:**

| Run | Commit | Status | Failure Point |
|-----|--------|--------|---------------|
| 287 | 3fba169 (V106 docs) | FAILURE | Step 15: Provision disposable database |
| 286 | 15296fd (V106 feat) | FAILURE | Step 15: Provision disposable database |
| 285 | f2c43bc | FAILURE | Step 15: Provision disposable database |
| 284 | 15296fd (retry) | FAILURE | Step 15: Provision disposable database |
| 283 | 84159e0 (V105 CI update) | FAILURE | Step 15: Provision disposable database |
| 282 | ae630cb (V105 checkpoint) | FAILURE | Step 16: EF Core + RLS checks |

**Pattern**: PostgreSQL 18.7 container (`postgres:18.7-bookworm`) fails to become healthy within 90 seconds (45 attempts × 2s). Step 15 "Provision disposable database and restricted roles" fails after ~1 second, suggesting the container health check passes but database provisioning fails immediately after.

**Local validation passes:**
- `git diff --check`: clean
- `dotnet build --configuration Release`: 0 warnings, 0 errors
- All test projects build successfully

## Still blocked or requiring decisions

- Appointment rescheduling/cancellation policy (requires product-owner decision)
- Outbox dispatcher and handlers (requires provider/credential/cost approvals)
- Data Protection key rotation (requires coordinated deployment plan)
- Platform Admin cross-tenant administration (requires explicit approval)
- WhatsApp Cloud API integration (requires Meta Business approval)
- Independent security review, backup/restore validation, trusted proxy configuration
- Orchestrator probe wiring to /health/live and /health/ready
- Deploy decisions

## Next steps

1. Monitor CI for spontaneous recovery (infrastructure flakiness may resolve)
2. If CI remains failed, investigate PostgreSQL 18.7 container health on GitHub Actions ubuntu-26.04
3. Update CONTINUE_HERE.md and manifest.json with current CI status
4. Create V108 plan if additional work needed

## Continuation

Read CLAUDE.md, docs/continuity/CONTINUE_HERE.md, this status, docs/continuity/manifest.json and docs/technical/ADMIN_ACTIONS_DESIGN_V1.md. Active worktree: C:\Users\iaras\Downloads\atendee-beauty-v85-work. Preserve C:\Users\iaras\Downloads\atendee-beauty-git untouched.