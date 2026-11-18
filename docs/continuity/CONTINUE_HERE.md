# CONTINUE HERE — Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23
- Backend onboarding: POST /api/v1/tenant/onboarding/complete; existing profile GET /api/v1/tenant.
- Owner with confirmed email and MFA moves tenant PendingVerification -> SetupRequired through migration 015, SECURITY DEFINER function, explicit transaction and SET LOCAL ROLE atendee_runtime. Existing status trigger audits. Retry is idempotent.
- V55 CI passed frontend, build, domain, PostgreSQL persistence/RLS and Identity/TOTP; HTTP tests failed later because the synthetic salon correctly remained SetupRequired and Professional sign-in is denied there.
- V56 adds test-only migrator reset to Active after onboarding assertions so subsequent unrelated professional/service tests use their expected fixture state.
- V56 full CI pending.
- Local C:\Temp\atendee-work is stale. Use GitHub branch and CI.
- Preserve V41 first-Owner provisioning. Do not modify main, deploy, configure SES or activate billing/trial.
