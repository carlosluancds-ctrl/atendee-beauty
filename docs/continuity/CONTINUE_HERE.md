# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest approved implementation checkpoint: `docs/continuity/CONTINUATION_STATUS_V61_2026-11-18.md`.
- V61 implementation commit: `a2169023009bf240e9ad4a3ca39fdd6be29305df`.
- V61 full CI passed: workflow run 44303068000.
- V61 continuity files are committed; see `docs/continuity/manifest.json`.
- Current V62 planning/inspection record: `docs/continuity/CONTINUATION_STATUS_V62_PLAN_2026-11-18.md`.
- V62 next: build browser E2E coverage. The portal is React 19/esbuild with a Node same-origin API proxy; current CI runs npm ci/audit/tests/build and a disposable PostgreSQL 18.7 + Identity/TOTP + API HTTP suite.
- No browser framework is currently declared. Add one only with a real npm-generated lockfile; do not handcraft integrity metadata or weaken npm ci/audit.
- Important V62 design decision: current HTTP test executable seeds synthetic identities inside WebApplicationFactory but does not leave a running API or browser-ready credentials. Decide and implement an isolated full-stack browser harness; if using mocked APIs initially, label it frontend-only and do not claim backend authorization coverage.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI.
- Do not deploy, configure SES, activate billing/trial or change production data without explicit authorization.
