# CONTINUE HERE — Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Latest code increment: V50 salon onboarding backend; CI pending at commit time.
- V50 added GET /api/v1/tenant and POST /api/v1/tenant/onboarding/complete.
- Migration 012 performs an idempotent owner-only PendingVerification -> SetupRequired transition through a narrowly granted SECURITY DEFINER function. Email confirmation and MFA are checked by the API; PostgreSQL checks active Owner membership and the status trigger audits the change.
- HTTP integration test now provisions an MFA Owner in PendingVerification, reads profile, completes onboarding and retries idempotently.
- The local C:\Temp\atendee-work snapshot is stale and must not be treated as the branch checkout. Use GitHub branch source and CI.
- Preserve V41 first-Owner provisioning and all previous continuity files. Do not change main, deploy, configure SES or activate billing/trial.
- Next: verify CI; fix issues; only then implement onboarding frontend. Do not invent legal/commercial requirements.
