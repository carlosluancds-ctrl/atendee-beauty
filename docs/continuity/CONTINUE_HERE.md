# CONTINUE HERE — Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- V50 added GET /api/v1/tenant and POST /api/v1/tenant/onboarding/complete.
- Migration 012 performs an idempotent active-Owner-only PendingVerification -> SetupRequired transition through a narrowly granted SECURITY DEFINER function; email confirmation and MFA are checked by the API; PostgreSQL status trigger audits the change.
- V50 CI run 44293354129 failed on a stale test symbol professionalProfileId, not the onboarding implementation.
- V51 fixes the stale test reference to assignedAssignedProfessionalProfileId; full CI result pending.
- Local C:\Temp\atendee-work snapshot is stale and must not be treated as the branch checkout. Use GitHub branch source and CI.
- Preserve V41 first-Owner provisioning and all prior continuity files. Do not change main, deploy, configure SES or activate billing/trial.
- Next: verify V51 CI; fix any actual backend/migration/test errors; only then implement onboarding frontend. Do not invent legal/commercial requirements.
