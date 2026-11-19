# Atendee Beauty — Continuity Status V82 (approved, 2026-11-19)

## Result

V82 adds explicit certificate-backed Data Protection key protection for production deployments that do not run on Windows. The implementation was validated on Linux by the complete GitHub Actions workflow against exact head `179c7753d10f2c7d88cce552bf7d12e0bf7c2da8`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44584885716)
- Run: `44584885716`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.
- Implementation commit: `179c7753d10f2c7d88cce552bf7d12e0bf7c2da8`.
- No production deployment, secret configuration or production data/configuration change was performed.

## Scope V82 — cross-platform Data Protection

Production behavior:

- A persistent `DataProtection:KeyRingPath` remains mandatory outside Development.
- Windows retains the existing DPAPI protection by default when no certificate is configured.
- Any production OS may explicitly select an RSA certificate using `DataProtection:CertificatePath` and `DataProtection:CertificatePassword`.
- The certificate is loaded from a PKCS#12/PFX file with ephemeral private-key storage; startup validates current validity, RSA public/private key availability and fails closed on missing password, unreadable certificate or invalid certificate.
- The persistent key ring is encrypted with the configured certificate via ASP.NET Core Data Protection.
- Non-Windows production fails startup closed when an explicit certificate path and password are absent.
- No certificate, password, key ring or production secret is committed.

Operational requirements:

- Supply the PFX and password through the deployment secret manager or protected mounted secret; never put them in appsettings or source control.
- Mount the key-ring directory on persistent storage with restrictive service-account ACLs/permissions. All replicas in one application share the same key ring, application name and active certificate.
- Back up the encrypted key ring and its matching private certificate securely and test restoration before production use.
- The current implementation configures one active certificate only. Do not replace/expire it blindly: existing key-ring entries still depend on the certificate that encrypted them. A coordinated certificate-rotation/decryption-key-ring procedure remains a deployment prerequisite and needs a separate reviewed increment.

## CI evidence — run 44584885716

All workflow steps completed successfully on exact head `179c7753d10f2c7d88cce552bf7d12e0bf7c2da8`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: success.
- Locked dependency restore and Release build: success, 0 warnings.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- New production Data Protection checks passed:
  - key XML contains encrypted key material (not plaintext master key);
  - a second production host can unprotect data protected before restart using the persistent key ring and certificate;
  - an incorrect PFX password fails startup closed;
  - Linux production without an explicit certificate fails startup closed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable credentials cleared and PostgreSQL container removed.

## Historical checkpoints preserved

V23–V81 remain unchanged. V79–V81 trusted-proxy protections and regression checks remain passing.

## Next work

Continue backend completion without crossing product/security approval boundaries. Prioritize a shared/distributed authentication rate limiter and its multi-instance concurrency tests; select no production Redis/managed service or deployment topology without a verified operational path. Continue endpoint authorization and integration coverage. Do not expose A1/A2 administrative endpoints until the product owner explicitly approves the actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.

## Guardrails

- `main` remains untouched.
- No deployment or production data/configuration changes.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Keep trusted-proxy allowlist deny-all unless exact proxy IPs are supplied through approved runtime configuration.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and independent TOTP replay guard.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval.

---
