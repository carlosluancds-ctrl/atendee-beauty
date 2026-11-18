# Atendee Beauty — Continuity Status V52 (2026-11-18)

## Scope
CI and migration sequencing correction for V50/V51 onboarding backend.

## Implemented
- GET /api/v1/tenant: current salon profile from trusted membership context.
- POST /api/v1/tenant/onboarding/complete: active Owner, lifecycle CompleteOnboarding permission, confirmed email, persisted MFA and signed amr=mfa.
- PostgreSQL SECURITY DEFINER function verifies active Owner membership, locks the tenant row, transitions PendingVerification (0) to SetupRequired (1), and treats retries in SetupRequired as idempotent.
- Existing tenant status trigger audits the state change atomically.
- HTTP integration test covers profile read, transition, audit and repeated request.
- V51 fixes a pre-existing stale test variable reference identified by CI run 44293354129.
- Migration sequencing corrected: existing migration 012_tenant_profile_projection already occupies version 012. Onboarding migration is now 015_complete_tenant_onboarding.sql, and CI workflow explicitly applies it after 014_create_professional_profiles.sql.

## CI history
- V50 commit 759e9fa88eff6cb147f736a0695a658eebd03998 failed run 44293354129 due to the stale test symbol.
- V51 commit 5f1f6a509ffde15e76f7b55a8c48a731b4c04d8b is expected to fix that symbol, but the workflow also revealed that migration application is an explicit list. V52 adds migration 015 to that list.
- Full CI for V52 is pending; no success is claimed.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Parent: 5f1f6a509ffde15e76f7b55a8c48a731b4c04d8b
- main untouched.

## Next
1. Wait for V52 CI and inspect build, migration and PostgreSQL HTTP integration outcomes.
2. Fix any newly exposed issues before frontend work.
3. After backend CI passes, build the onboarding UI without inventing commercial, legal or billing requirements.
