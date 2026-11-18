# Atendee Beauty — Continuity Status V56 (2026-11-18)

## Scope
Keep the onboarding integration fixture isolated from later service/professional checks.

## Backend implementation
- GET /api/v1/tenant is the existing authenticated profile endpoint.
- POST /api/v1/tenant/onboarding/complete requires active Owner, lifecycle permission, confirmed email, persisted MFA and signed amr=mfa.
- Migration 015 is explicitly applied after 014. SECURITY DEFINER function verifies active Owner membership, locks the tenant row and performs idempotent PendingVerification -> SetupRequired.
- API calls the function within an explicit transaction after SET LOCAL ROLE atendee_runtime.
- PostgreSQL tenant status trigger audits the change atomically.
- V53 CI run 44294027003 demonstrated profile, onboarding transition, trigger audit and idempotent retry all pass, then failed later because the same synthetic tenant had correctly moved to SetupRequired and a Professional was no longer allowed to sign in.
- V55 fixed HTTPS in that test client; V56 now resets the synthetic tenant to Active via the test-only migrator after onboarding assertions so later unrelated service/professional tests run under their expected fixture status. This reset is not an application endpoint or production behavior.

## CI
- V55 run 44294386866 passed frontend build, .NET restore/build, domain checks, PostgreSQL 18.7 startup/provisioning, persistence/RLS checks and Identity/TOTP checks.
- V55 failed only in a later Professional login assertion because the shared test tenant was left in SetupRequired. V56 adds fixture reset after the onboarding assertions.
- Full CI for V56 is pending.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Parent: 81330b8574ce6e487c75d441c261f375e9de3d09.
- main untouched.

## Next
Verify full CI for V56. If all checks pass, proceed to salon onboarding UI without introducing trial, legal or billing rules.
