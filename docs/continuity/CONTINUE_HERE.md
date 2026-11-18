# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest approved checkpoint: `docs/continuity/CONTINUATION_STATUS_V62_2026-11-18.md`.
- Latest V62 implementation commit: `6ab1560e452776c6f1d4ccfc8900a4f04b47f48f`.
- V62 complete CI passed: workflow run 44309710988. This includes Chromium install, 8 browser E2E cases, frontend audit/unit/build, .NET Release/domain checks, PostgreSQL 18.7/RLS, Identity/TOTP and API HTTP security checks.
- Browser suite: `web/atendee-portal/tests/e2e/portal.spec.mjs`; Playwright `1.65.1` pinned with npm-generated lockfile. CI uses `npx playwright install --with-deps chromium` and `npm run test:e2e`.
- Important coverage boundary: browser tests mock API responses. They verify real React/browser flows, role-conditioned rendering, request bodies/methods and antiforgery headers, but do not prove backend authorization or tenant isolation. Those remain covered by real API/Identity/PostgreSQL/RLS integration tests.
- Next: V63 full-stack browser harness with real ASP.NET API + portal + disposable PostgreSQL and isolated synthetic identities/MFA. Inspect current `WebApplicationFactory`, API HTTP fixture, database provisioning and Identity test helpers before designing the seed/host handoff. Do not create production backdoors or weaken existing security checks.
- V62 first CI run 44308963259 failed only because Playwright 1.56.1 did not support Ubuntu 26.04; upgraded to 1.65.1. Final run 44309710988 succeeded.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI.
- Do not deploy, configure SES, activate billing/trial or change production data without explicit authorization.
