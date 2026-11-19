# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V68_2026-11-19.md` (approved).
- Latest approved implementation: V68, validated commit `2065f4ecab9969795f4dc6be7af3376cc51340c1`.
- V68 CI run `44455491808` completed successfully. It proves forged tenant/actor/role/status headers and query parameters do not override persisted request context, and a Receptionist cannot complete Owner onboarding using forged headers/body (HTTP 403).
- V65 service isolation, V66 professional identity/profile isolation, V67 cross-tenant professional-service association rejection, and V64 antiforgery session-transition assertions remain in the passing full-stack flow.
- Previous continuity files V23–V67 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first with a review of identity, authentication, onboarding and tenant-context flows; verify side effects use server-resolved identity/tenant, mandatory audit and appropriate transactions.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
