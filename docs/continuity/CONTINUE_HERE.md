# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V67_2026-11-19.md` (approved).
- Latest approved implementation: V67, validated commit `962649067d02f4d65e347025c16b5cd069927565`.
- V67 CI run `44454896396` completed successfully. It validates that Salon B cannot assign Salon A's service to its professional profile and Salon A cannot assign Salon B's service to its profile; both return HTTP 400 `professional_services_validation_failed`, and both profiles remain unchanged.
- V65 service read/update isolation, V66 professional identity/profile isolation, and V64 antiforgery session-transition assertions remain in the passing real Kestrel/PostgreSQL full-stack flow.
- Previous continuity files V23–V66 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first with a systematic review of existing endpoints and stores for any resource, identity or association lookup lacking trusted tenant scope; add negative tests where needed.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
