# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V70_2026-11-19.md` (approved).
- Latest approved implementation: V70, validated commit `49b8e0bb235905f7d7d3663419fe9c2eb337214c`.
- V70 CI run `44457062380` passed. Failed-password verification, AccessFailedCount update and `operational_auth.login_failed` audit now share one transaction; a wrong password increments the counter and successful password verification resets it before MFA setup.
- V69 onboarding actor provenance and atomic TOTP failure auditing remain covered; V65–V68 tenant isolation and trusted-context protections remain in the passing CI flow.
- Previous continuity files V23–V69 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first auditing identity/account-security operations that combine persistence with mandatory audit events; make failure paths atomic without weakening lockout or authentication rules.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
