# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V73_2026-11-19.md` (approved).
- Latest approved implementation: V73, validated commit `eb38175ef2b3b6a325535740b99f27f9fcbd28e6`.
- V73 CI run `44461641990` passed completely. Password login, TOTP challenge, recovery-code challenge and MFA enrollment now lock/reload the Identity row before changing/resetting failed-attempt state. Cross-flow HTTP/PostgreSQL regressions prove concurrent failures are all counted/audited and locked accounts do not create additional failure events.
- The TOTP replay guard remains a separate atomic compare-and-set transaction.
- V72 sequential/concurrent login lockout tests remain green.
- V71 pending-MFA audit failure regression remains green: failed mandatory audit leaves no usable temporary two-factor cookie or application session.
- V70 failed-password counter and `operational_auth.login_failed` audit remain atomic.
- V69 onboarding actor provenance and failed-TOTP audit atomicity remain green.
- V65 service isolation, V66 professional identity/profile isolation, V67 professional-service assignment isolation, and V68 trusted-context spoofing protections remain in passing CI.
- Previous continuity files V23–V72 are preserved. Do not rewrite historical checkpoints.
- Next: review account disable/session-revocation racing with in-flight login and MFA completion. Prove a disabled/revoked identity cannot receive a usable new application/setup/challenge session after the state change; preserve audit-before-cookie ordering and the independent TOTP replay guard.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
