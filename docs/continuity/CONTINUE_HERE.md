# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest work-in-progress checkpoint: `docs/continuity/CONTINUATION_STATUS_V64_2026-11-18.md`.
- Latest approved checkpoint: `docs/continuity/CONTINUATION_STATUS_V63_2026-11-18.md`.
- Latest approved implementation commit: `e106c5926b93168ca4e0c9b7b2a6e762fc6606a4`; V63 full CI and continuity CI both passed (runs 44319685006 and 44320319677).
- V64 work-in-progress commits: `b27b4030bb8f8a44a6184d1fc8acaac3f8b6c8f9` and `aa38361ed16017a9642145400a94d9b3a4088286`.
- V64 adds real-browser checks that an anonymous antiforgery request token is rejected after Owner login, and an authenticated token is rejected after logout. Both expect HTTP 400 `antiforgery_validation_failed`.
- V64 is NOT approved. Runs 44321104904 and 44321314420 failed before any job steps/logs were available; a retry had the same result. No compiler/test diagnostic was produced. First restore a diagnosable GitHub Actions run, then verify the assertions.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Next after V64 CSRF regression: add explicit two-tenant negative full-stack browser cases without weakening API/RLS checks.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI.
- Do not deploy, configure SES, activate billing/trial or change production data without explicit authorization.
