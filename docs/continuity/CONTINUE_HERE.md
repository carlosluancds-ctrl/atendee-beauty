# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V69_2026-11-19.md` (approved).
- Latest approved implementation: V69, validated commit `00c4b55955738b06602b8cbb0519f6f3ef60ebf4`.
- V69 CI run `44456485697` passed. Onboarding status audit now records the exact authenticated Owner actor from trusted request context; invalid TOTP and replayed recovery-code attempts each atomically increment failed attempts and write one audit event; successful TOTP resets the counter.
- V65 service isolation, V66 professional identity/profile isolation, V67 professional-service association isolation, and V68 trusted-context spoofing protections remain in passing CI.
- Previous continuity files V23–V68 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first reviewing authentication/MFA operations that combine state changes and audit events; require atomicity and trusted actor provenance.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
