# Atendee Beauty — Continuity Status V57 (2026-11-18)

## Scope
Isolate the role-promotion HTTP integration scenario from login rate-limit budgets used by other test scenarios.

## Verified V56 results
- Frontend dependency restore/audit/tests/build passed.
- Locked .NET restore, Release build and domain checks passed.
- PostgreSQL 18.7 startup and restricted role provisioning passed.
- EF Core/PostgreSQL RLS checks passed.
- Identity/TOTP/tenant-context checks passed.
- HTTP onboarding assertions passed: current profile, Owner completion, status transition audit and idempotent retry.
- Professional login/profile/service-assignment checks also passed after the test fixture reset to Active.
- V56 then failed at a later receptionist role-promotion scenario because it shared the already-used MFA test host's per-IP login rate-limit budget.

## V57 change
- Creates a dedicated WebApplicationFactory and Data Protection key ring for the role-promotion test.
- Moves the receptionist login/role-promotion test to this isolated host; does not weaken the production login rate limit.
- Disposes the dedicated factory and removes its temporary key ring during test cleanup.
- No production API, database or frontend behavior changed.

## CI
- V56 run 44294775155 failed only at the later role-promotion test's initial receptionist login; all onboarding and preceding integration assertions passed.
- V57 full CI pending.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Parent: da49ec156fb0e8cbbb0993e5cbdbe695fa98c7e3.
- main untouched.

## Next
Verify V57 full CI. If it passes, record the verified backend milestone and proceed with the onboarding frontend.
