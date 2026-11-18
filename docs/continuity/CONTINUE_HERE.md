# CONTINUE HERE — Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Backend onboarding: POST /api/v1/tenant/onboarding/complete; existing GET /api/v1/tenant.
- Active Owner with confirmed email and MFA completes PendingVerification -> SetupRequired through migration 015, SECURITY DEFINER function, explicit transaction and SET LOCAL ROLE atendee_runtime. Status trigger audits; retry is idempotent.
- V56 passed frontend, Release build, domain, PostgreSQL 18.7, persistence/RLS, Identity/TOTP, onboarding, audit, retry and professional checks, then failed at a later role-promotion test due to shared login rate-limit budget.
- V57 moves the role-promotion test to a dedicated WebApplicationFactory/key ring; production rate limits unchanged. Full CI pending.
- Local C:\Temp\atendee-work is stale. Use GitHub branch and CI.
- Preserve V41 first-Owner provisioning. Do not modify main, deploy, configure SES or activate billing/trial.
