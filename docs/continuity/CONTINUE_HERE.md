# CONTINUE HERE — Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- GET /api/v1/tenant already exists in OperationalAuthenticationEndpoints. V53 removed duplicate route registration.
- POST /api/v1/tenant/onboarding/complete is the new active-Owner-only transition PendingVerification -> SetupRequired, requiring confirmed email and MFA.
- Migration 015 is explicitly applied by the PostgreSQL CI workflow after migration 014; SECURITY DEFINER function verifies active Owner membership and trigger audits status change.
- V54 tightens execution: API starts transaction, SET LOCAL ROLE atendee_runtime, then calls function. Migration grants execute to runtime role only; app_login gets no direct schema/table access.
- V50/V51/V52/V53 CI failures and fixes are detailed in versioned continuity statuses. V54 CI pending.
- Local C:\Temp\atendee-work is a stale snapshot. Use GitHub branch and CI as source of truth.
- Preserve V41 first-Owner provisioning. Do not modify main, deploy, configure SES or activate billing/trial.
- Next: verify V54 CI; fix backend/migration/test issues before frontend implementation.
