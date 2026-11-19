# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V81_2026-11-19.md` (approved).
- Latest validated head: `aa84099feb65d6cb6b9214e9745d0d98b1648a86`.
- V81 CI run `44580208061` passed completely on that exact head: frontend audit/tests/build, 8 portal browser E2E tests, Release build, 26 domain checks, 10 PostgreSQL/RLS checks, 49 Identity/TOTP checks, HTTP authentication/session-security checks and real Kestrel + PostgreSQL full-stack browser test. Disposable credentials were cleared and the PostgreSQL container was removed.
- V81 positive trusted-proxy checks passed for IPv6 loopback (`::1`), IPv4 loopback (`127.0.0.1`) and IPv4-mapped IPv6 loopback (`::ffff:127.0.0.1`), which is canonicalized to IPv4 before storage and middleware use.
- V80 continues to reject nine invalid proxy addresses, including IPv4 multicast/reserved/broadcast, IPv6 multicast/unspecified and mapped invalid forms.
- V79 trusted-proxy protections remain: deny-all by default, exact `ForwardedHeaders:KnownProxies` allowlist, one forwarding hop, symmetric `X-Forwarded-For`/`X-Forwarded-Proto`, and no trust in `X-Forwarded-Host`. Configure actual proxy IPs only through approved runtime environment settings.
- V78 confirms `IOperationalAccountSecurityService` is registered internally but has no API endpoint call sites. Do not expose `DisableAsync` or `RevokeSessionsAsync` directly: the service does not perform tenant/role authorization itself.
- `docs/technical/ADMIN_ACTIONS_DESIGN_V1.md` remains a proposal. Explicit owner decisions are required before A1/A2 administrative endpoints.
- Historical checkpoints V23–V80 are preserved. Do not rewrite historical checkpoints.
- Next: continue unrelated backend work without crossing the administrative authorization boundary. No A1/A2 endpoint until explicit owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.
- Do not deploy, configure SES, activate billing/trial, change production data/configuration, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.

---
