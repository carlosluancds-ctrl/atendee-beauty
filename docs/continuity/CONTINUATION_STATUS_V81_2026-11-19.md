# Atendee Beauty — Continuity Status V81 (approved, 2026-11-19)

## Result

V81 approved after the complete PostgreSQL 18 persistence integration workflow passed on validation head `aa84099feb65d6cb6b9214e9745d0d98b1648a86`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44580208061)
- Run: `44580208061`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.
- Test commit: `aa84099feb65d6cb6b9214e9745d0d98b1648a86`.
- The increment adds positive HTTP regression coverage for canonical trusted-proxy address forms. It does not change application behavior. No production deployment or production configuration change was performed.

## Scope V81 — positive trusted-proxy address forms

The HTTP integration checks now verify that each of these configured proxy addresses is accepted and produces the expected canonical entry in `ForwardedHeadersOptions.KnownProxies`:

- `::1` → IPv6 loopback.
- `127.0.0.1` → IPv4 loopback.
- `::ffff:127.0.0.1` → canonicalized IPv4 loopback.

The mapped IPv4 case is the final configured case used by the subsequent middleware test, which confirms that the canonicalized IPv4 loopback can supply the client IP and HTTPS scheme while `X-Forwarded-Host` remains ignored. Each test restores its environment override in a `finally` block.

## Complete CI evidence — run 44580208061

All workflow steps completed successfully on exact validation head `aa84099feb65d6cb6b9214e9745d0d98b1648a86`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore: success.
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- Positive trusted-proxy address forms: IPv6, IPv4 and IPv4-mapped IPv6 all passed.
- V80 invalid-address startup checks: all nine cases passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable database credentials cleared and PostgreSQL container removed.

## Historical checkpoints preserved

V23–V80 remain unchanged. V80's canonicalization and non-unicast rejection remain passing. V79's deny-all default, exact proxy allowlist, one-hop forwarding and host-header protections remain passing. V78's administrative authorization boundary remains in force.

## Next step

Continue backend-first on unrelated safe work. Do not add A1/A2 administrative HTTP endpoints until the product owner explicitly approves actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.

## Guardrails

- `main` remains untouched.
- No deployment or production data/configuration changes.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Keep the trusted-proxy allowlist empty unless exact proxy IPs are supplied through approved runtime configuration.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.

---
