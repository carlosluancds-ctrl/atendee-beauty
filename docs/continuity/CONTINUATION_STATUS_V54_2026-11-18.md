# Atendee Beauty — Continuity Status V54 (2026-11-18)

## Scope
Tighten the database execution boundary for salon onboarding completion.

## Implementation
- Onboarding completion API now opens an explicit tenant DB transaction and uses SET LOCAL ROLE atendee_runtime before invoking the SECURITY DEFINER function.
- Migration 015 grants function execution to atendee_runtime, not atendee_app_login, and no longer grants schema USAGE directly to the login role.
- This follows the existing tenant transaction security model: atendee_app_login has no direct table/schema access and can assume the restricted runtime role only within a transaction.
- Existing trusted actor/tenant derivation, Owner/MFA/email checks, row lock, idempotency and audit trigger remain unchanged.
- Existing GET /api/v1/tenant route is retained; V53 removed the duplicate registration.

## CI
- V53 full CI is pending at commit time. Earlier V51 failed due duplicate route; V52 also included that route and is expected to fail for the same reason.
- V54 is the latest code commit and requires a fresh full CI run. No success is claimed.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Parent: d0d2e8aa29dad8e7df3a5b7fd4b4980e324f4f88.
- main untouched.

## Next
Verify full CI for V54, including migration 015 and API HTTP tests. Only after success proceed to onboarding UI.
