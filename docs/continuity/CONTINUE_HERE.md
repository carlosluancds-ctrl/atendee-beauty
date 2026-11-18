# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest checkpoint: `docs/continuity/CONTINUATION_STATUS_V61_2026-11-18.md`.
- Latest implementation commit: `a2169023009bf240e9ad4a3ca39fdd6be29305df`.
- V61 full CI passed: workflow run 44303068000 (all frontend, Release, domain, PostgreSQL 18.7/RLS, Identity/TOTP and API HTTP checks succeeded).
- V60 added tenant-scoped eligible Professional membership discovery for Owner/Manager and a Professional-only endpoint returning assigned service details.
- V61 adds explicit Manager MFA/authorization integration coverage and currency-minor-unit-safe price display.
- Next: V62 browser E2E. Inspect routing, dev-server/API integration and CI before selecting/adding Playwright; it is not currently declared in portal package.json.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial or change production data without explicit authorization.
