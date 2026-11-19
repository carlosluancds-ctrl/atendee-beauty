# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V66_2026-11-19.md` (approved).
- Latest approved implementation: V66, validated commit `90c6b6c20a3b881395d07d335dda32eabaff009a`.
- V66 CI run `44454083316` completed successfully. It validates isolated professional memberships in two salons, eligible-member discovery scoped per tenant, rejection of cross-tenant professional-profile creation in both directions (400 `professional_membership_invalid`), and rejection of update to a known foreign profile ID (404).
- V65 service isolation and V64 antiforgery session-transition assertions remain in the same passing real Kestrel/PostgreSQL full-stack browser flow.
- Previous continuity files V23–V65 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first by reviewing professional-service association and all existing endpoint resource/identity lookups for tenant scoping; add negative cross-tenant mutation tests.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
