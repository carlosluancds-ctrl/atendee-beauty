# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V71_2026-11-19.md` (approved).
- Latest approved implementation: V71, validated commit `63ecaa52f8cab3699990bd767d84233717fa755e`.
- V71 CI run `44458718974` passed completely. The HTTP integration log explicitly confirms a failed mandatory pending-MFA audit aborts login, does not persist the event, leaves no usable temporary two-factor cookie, and cannot be upgraded to an application session.
- V70 failed-password counter and `operational_auth.login_failed` audit remain atomic and passing.
- V69 onboarding actor provenance and failed-TOTP audit atomicity remain green.
- V65 service isolation, V66 professional identity/profile isolation, V67 professional-service assignment isolation, and V68 trusted-context spoofing protections remain in passing CI.
- Previous continuity files V23–V70 are preserved. Do not rewrite historical checkpoints.
- Next: review the `alreadyLockedOut` snapshot and audit-volume policy in `OperationalAuthenticationEndpoints.LoginAsync`. Verify sequential and concurrent behavior at the configured five-attempt lockout threshold; preserve audit of the threshold-causing failure and suppress unbounded events only for attempts against an already-locked account. Add integration tests if evidence shows a gap.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
