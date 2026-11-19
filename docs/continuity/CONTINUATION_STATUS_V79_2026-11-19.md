# Atendee Beauty — Continuity Status V79 (approved, 2026-11-19)

## Result

V79 approved after the complete PostgreSQL 18 persistence integration workflow passed on validation head `a8e9ce8196d66c3699b0625d3991bd3d6f19f53d`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44578183985)
- Run: `44578183985`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.
- Implementation commit: `8232d4e48ce7e9b4445f05e02813ad83fc8bd633`.
- Final validation/test commit: `a8e9ce8196d66c3699b0625d3991bd3d6f19f53d`.
- The increment secures trusted forwarded-header handling and adds HTTP-level regression coverage. No production deployment or production configuration change was performed.

## Scope V79 — trusted forwarded proxies

### Application behavior

- The API reads the exact trusted proxy IP allowlist from `ForwardedHeaders:KnownProxies`.
- Each configured value must parse as a valid unicast IP address. Unspecified addresses, IPv4 broadcast and IPv6 multicast are rejected at startup.
- The allowlist is empty by default. Because an empty ASP.NET Core `KnownProxies` collection disables the address check, the default configuration inserts the unmatchable unspecified-address sentinels `IPAddress.Any` and `IPAddress.IPv6Any`, preserving deny-all behavior for real proxies.
- `KnownIPNetworks` and `KnownProxies` are cleared before the explicit allowlist is applied.
- Only `X-Forwarded-For` and `X-Forwarded-Proto` are enabled. The forwarding limit is one hop and header symmetry is required.
- `X-Forwarded-Host` is not enabled and cannot override the request host.
- `UseForwardedHeaders` remains before middleware that relies on the effective client IP or scheme.
- Actual proxy IPs must be configured explicitly through the runtime environment. No proxy IP was assumed, committed or configured for production.

### HTTP regression coverage

The HTTP integration checks verify:

- Default configuration trusts no real proxy.
- Spoofed forwarded IP, scheme and host headers from an untrusted peer do not change the request origin or rate-limit partition.
- A configured exact proxy IP is trusted for exactly one forwarding hop.
- A trusted proxy can provide client IP and HTTPS scheme, while `X-Forwarded-Host` remains ignored.
- Invalid trusted-proxy configuration fails application startup closed.

The test supplies startup-time configuration through `ForwardedHeaders__KnownProxies__0` before the test application is created, because `Program.cs` reads the allowlist before `WebApplicationFactory.ConfigureAppConfiguration` executes. The test restores the prior environment value in `finally` blocks. IP address equality is checked by value using `IPAddress.Equals`.

## Complete CI evidence — run 44578183985

All workflow steps completed successfully on the exact validated head `a8e9ce8196d66c3699b0625d3991bd3d6f19f53d`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore: success.
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- Forwarded-header checks: default deny-all, untrusted-header rejection, explicit proxy acceptance, one-hop limit, host-header rejection and invalid-configuration startup rejection all passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable database credentials cleared and PostgreSQL container removed.

The earlier failed CI attempts were not approved. The successful run above validates the final code and tests; no failed run was treated as approval.

## Historical checkpoints preserved

V23–V78 remain unchanged. V78's administrative authorization review remains valid: `IOperationalAccountSecurityService` is registered internally but has no API endpoint call sites, and its disable/revoke operations must not be exposed directly because it does not enforce tenant/role authorization.

## Next step

Continue backend-first on an unrelated safe area. Do not add A1/A2 administrative HTTP endpoints until the product owner explicitly approves actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.

## Guardrails

- `main` remains untouched.
- No deployment or production data/configuration changes.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Keep the trusted-proxy allowlist empty unless exact proxy IPs are supplied through an approved runtime configuration.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.

---
