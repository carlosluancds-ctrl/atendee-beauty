# Atendee Beauty — Continuity Status V51 (2026-11-18)

## Scope
CI follow-up for V50 backend onboarding implementation.

## V50 implementation
- GET /api/v1/tenant returns the current salon profile from trusted membership context.
- POST /api/v1/tenant/onboarding/complete requires active Owner, CompleteOnboarding policy, confirmed email, TwoFactorEnabled and signed amr=mfa.
- Migration 012 adds a narrowly granted SECURITY DEFINER transition PendingVerification -> SetupRequired, verifies active Owner membership, locks the tenant row and is idempotent.
- Existing PostgreSQL status trigger records the transition atomically.
- HTTP integration checks cover profile, successful completion, audit and idempotent retry.

## CI result and fix
- V50 commit 759e9fa88eff6cb147f736a0695a658eebd03998 failed GitHub Actions run 44293354129 at the Build Release step.
- The compiler failure was a stale test variable reference professionalProfileId; the value had already been renamed to assignedAssignedProfessionalProfileId in the same test block.
- V51 fixes only that test reference. No production source or migration logic changed in this fix.
- A new full CI run is expected on this commit; its result must be recorded after it finishes. Do not claim validation until then.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Parent: 759e9fa88eff6cb147f736a0695a658eebd03998.
- Main remains untouched.

## Next
1. Verify the new CI build, migration and PostgreSQL HTTP integration checks.
2. Correct any real errors revealed by the suite.
3. After backend success, build the salon onboarding UI. No commercial trial, legal acceptance, billing, SES or deployment changes are authorized by this increment.
