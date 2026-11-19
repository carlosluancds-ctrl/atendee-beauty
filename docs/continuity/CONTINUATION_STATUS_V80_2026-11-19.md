# Atendee Beauty — Continuity Status V80 (approved, 2026-11-19)

## Result

V80 approved after the complete PostgreSQL 18 persistence integration workflow passed on validation head `48ef700aeb1dd082e8b2386dbe33c14fefaff2bb`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44579219902)
- Run: `44579219902`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.
- Implementation and regression-test commit: `48ef700aeb1dd082e8b2386dbe33c14fefaff2bb`.
- This increment tightens trusted-proxy address validation. No production deployment or production configuration change was performed.

## Scope V80 — reject non-unicast trusted proxy addresses

### Application behavior

- Configured `ForwardedHeaders:KnownProxies` values must parse as IP addresses.
- IPv4-mapped IPv6 addresses are canonicalized to their IPv4 representation before validation and storage. This prevents mapped forms from bypassing IPv4 address-class checks and ensures a single canonical allowlist representation.
- Unspecified addresses, IPv4 broadcast, IPv4 multicast/reserved ranges (first octet 224–255), and IPv6 multicast addresses are rejected during startup.
- The accepted collection therefore contains only exact, usable unicast proxy addresses.
- Existing V79 behavior remains unchanged: empty allowlist is deny-all, only `X-Forwarded-For` and `X-Forwarded-Proto` are enabled, one forwarding hop is accepted, header symmetry is required, and `X-Forwarded-Host` is not trusted.

### HTTP regression coverage

Startup is verified to fail closed for each of these configured values:

- `not-an-ip`
- `0.0.0.0`
- `224.0.0.1` (IPv4 multicast)
- `240.0.0.1` (IPv4 reserved range)
- `255.255.255.255` (IPv4 broadcast)
- `::` (IPv6 unspecified)
- `ff02::1` (IPv6 multicast)
- `::ffff:0.0.0.0` (IPv4-mapped unspecified)
- `::ffff:224.0.0.1` (IPv4-mapped multicast)

Each case passed in the HTTP integration checks. Environment overrides are restored in `finally` blocks.

## Complete CI evidence — run 44579219902

All workflow steps completed successfully on exact validation head `48ef700aeb1dd082e8b2386dbe33c14fefaff2bb`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore: success.
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- V79 forwarded-header trust tests and all nine invalid proxy-address startup checks: passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable database credentials cleared and PostgreSQL container removed.

## Historical checkpoints preserved

V23–V79 remain unchanged. V79's trusted-proxy deny-all default, exact allowlist, one-hop forwarding and host-header protections remain passing. V78's administrative authorization boundary remains in force: the internal disable/revoke service must not be exposed through HTTP endpoints until explicit owner decisions are approved.

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