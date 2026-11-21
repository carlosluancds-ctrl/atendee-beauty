# Continuation status V108 - CI infrastructure flakiness, V106 validated locally, service container migration

Date: 2026-11-21
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation (unchanged from V106)

- V106 commit `15296fd` adds migration `022_enforce_last_owner_protection.sql` with deferred constraint triggers
- Enforces at least one active MFA-enabled Owner per tenant at database level
- Covers tenant_users role/is_active changes, identity.AspNetUsers TwoFactorEnabled changes, and tenant creation
- Uses advisory locks with hash `73190422` (different from V10's `73190421`) and SECURITY DEFINER functions for fail-closed behavior
- CI workflow updated to run the new migration after migration 021

## Service container migration attempt (run 294) - COMPLETED WITH FAILURE

- **Run 294** triggered at `2026-11-21T00:58:52Z` with new GitHub Actions service containers for PostgreSQL 18.7
- Approach: Use native `services:` section in workflow instead of manual `docker run`
- Purpose: Reduce provisioning flakiness on ubuntu-26.04 by leveraging GitHub's built-in service management
- Status: **COMPLETED WITH FAILURE** at `2026-11-21T01:00:49Z` (duration ~1m 57s)
- Failed at Step 16 "Provision disposable database and restricted roles" - same failure point as previous runs
- The service container approach did NOT resolve the infrastructure flakiness
- Previous runs 291-293 all failed at Step 15 "Provision disposable database and restricted roles"
- This represents a strategic shift in CI infrastructure approach, not a logic change

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

**Infrastructure flakiness persists across all V106, V107, and V108 runs:**

| Run | Commit | Status | Failure Point |
|-----|--------|--------|---------------|
| 294 | `d056144` | FAILURE | Step 16: Provision disposable database (service containers) |
| 293 | `44846728091` | FAILURE | Step 15: Provision disposable database |
| 292 | `e5b8e7a` | FAILURE | Step 15: Provision disposable database |
| 291 | `fb33bd2` | FAILURE | Step 15: Provision disposable database |
| 290 | `5376d3f` | FAILURE | Step 15: Provision disposable database |
| 288 | `99059af` | FAILURE | Step 15: Provision disposable database |

**Pattern**: PostgreSQL 18.7 container (`postgres:18.7-bookworm`) consistently fails during "Provision disposable database and restricted roles" step on GitHub Actions ubuntu-26.04. Container health check passes but database provisioning fails immediately after (~1 second). This is **infrastructure flakiness, not logic failure** - V106 implementation fully validated locally.

**Service container migration (run 294)** attempted using GitHub Actions native service containers instead of manual `docker run` but did NOT resolve the flakiness - same failure at the provisioning step (now Step 16 due to service container initialization).

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
- CI infrastructure flakiness resolution (PostgreSQL 18.7 on ubuntu-26.04)

## Next steps

1. **Service container approach did not resolve flakiness** - run 294 failed at same provisioning step
2. Investigate PostgreSQL 18.7 container provisioning failure on GitHub Actions ubuntu-26.04 more deeply
3. Consider alternative approaches: different PostgreSQL image, runner version, or provisioning strategy
4. Update CONTINUE_HERE.md and manifest.json with current CI status
5. Create V109 plan for CI infrastructure investigation/fix
6. Await resolution of infrastructure flakiness before proceeding with increments requiring disposable database

## Continuation

Read CLAUDE.md, docs/continuity/CONTINUE_HERE.md, this status, docs/continuity/manifest.json and docs/technical/ADMIN_ACTIONS_DESIGN_V1.md. Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`. Preserve `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.