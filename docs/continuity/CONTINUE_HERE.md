# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V65_2026-11-19.md` (approved).
- Latest approved implementation: V65, validated commit `0a06ab045691a336c6114bf173f1383e3aafb4ac`; continuity documentation commits: V65 status `0d2d08aa89b0dd9c7ee81b688e9d7a7691bce88b`, index `4bf33dbeb4e9ba21d9352480a90757542ea82811`, manifest reconciliation `51019009b4e27cbd88ceea5025158d861b026a8c`.
- V65 CI run `44451964941` completed successfully. It validates two independent salon tenants, tenant-scoped service listings, cross-tenant service read/update rejection in both directions (HTTP 404), RLS, antiforgery session transitions, frontend, Release build, domain checks, Identity/TOTP, PostgreSQL 18.7 and full-stack browser execution.
- V64 stale antiforgery token assertions were exercised in the same passing full-stack run. See V65 status for evidence and note about the preserved historical V64 document.
- Previous continuity files V23–V64 are preserved. Do not rewrite historical checkpoints.
- Next: continue backend-first with a focused review and negative tests for remaining cross-tenant read/mutation surfaces. Read approved tenant-status and authorization policies before changing scope or policy.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
