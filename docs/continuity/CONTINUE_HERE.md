# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V79_2026-11-19.md` (approved).
- Latest validated head: `a8e9ce8196d66c3699b0625d3991bd3d6f19f53d`.
- V79 CI run `44578183985` passed completely on that exact head: frontend audit/tests/build, 8 portal browser E2E tests, Release build, 26 domain checks, 10 PostgreSQL/RLS checks, 49 Identity/TOTP checks, HTTP authentication/session-security checks, forwarded-proxy trust tests and real Kestrel + PostgreSQL full-stack browser test. Disposable credentials were cleared and the PostgreSQL container was removed.
- V79 configures forwarded headers to trust only exact IPs explicitly listed in `ForwardedHeaders:KnownProxies`. The default is deny-all using unmatchable unspecified-IP sentinels; only X-Forwarded-For/X-Forwarded-Proto are enabled, with one hop and header symmetry. X-Forwarded-Host is not trusted. Invalid proxy configuration fails startup closed.
- Configure actual proxy IPs only through approved runtime environment settings. No proxy IP was assumed or added to production configuration.
- V78 confirms `IOperationalAccountSecurityService` is registered internally but has no API endpoint call sites. Do not expose `DisableAsync` or `RevokeSessionsAsync` directly: the service does not perform tenant/role authorization itself.
- `docs/technical/ADMIN_ACTIONS_DESIGN_V1.md` remains a proposal. Explicit owner decisions are required before A1/A2 administrative endpoints.
- Historical checkpoints V23–V78 are preserved. Do not rewrite historical checkpoints.
- Next: continue unrelated backend work without crossing the administrative authorization boundary. No A1/A2 endpoint until explicit owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.
- Do not deploy, configure SES, activate billing/trial, change production data/configuration, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.

---
