# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest approved checkpoint: `docs/continuity/CONTINUATION_STATUS_V63_2026-11-18.md`.
- Latest V63 implementation commit: `e106c5926b93168ca4e0c9b7b2a6e762fc6606a4`.
- V63 complete CI passed: workflow run 44319685006. It includes npm audit/unit/build, 8 mocked Chromium browser tests, locked .NET restore/Release/domain checks, disposable PostgreSQL 18.7, EF Core/RLS, Identity/TOTP, API HTTP security checks and 1 full-stack browser test against the real Kestrel API and PostgreSQL.
- Full-stack harness: `tests/Atendee.Api.HttpChecks/BrowserE2eRunner.cs`; full-stack spec: `web/atendee-portal/tests/e2e/fullstack.spec.mjs`; config: `web/atendee-portal/playwright.fullstack.config.mjs`.
- Real browser flow covers Owner TOTP + service creation, Manager MFA enrollment + Professional profile/service assignment, Professional own-services view and Receptionist read-only/403 behavior.
- Important V63 security correction: API runs authentication before antiforgery validation so request tokens are checked against the current claims principal. HTTP tests fetch fresh antiforgery tokens after login/MFA/logout transitions. All unsafe requests still fail closed.
- Coverage boundary: 8 mocked browser tests verify UI behavior; 1 full-stack browser test uses real API/PostgreSQL. API HTTP, Identity and PostgreSQL/RLS checks remain authoritative for backend authorization and tenant isolation.
- Next: V64 explicit two-tenant negative browser cases and authenticated antiforgery/session-transition regression cases. Do not add production-only test switches or weaken RLS/backend authorization.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI.
- Do not deploy, configure SES, activate billing/trial or change production data without explicit authorization.
