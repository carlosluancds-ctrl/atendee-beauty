# Security review — operational identity hardening (V3)

**Review date:** 2026-11-18  
**Repository:** `carlosluancds-ctrl/atendee-beauty`  
**Base branch:** `wip/operational-identity-v1`  
**Base commit:** `6a9d1795b4c30238c1ad1399b0298765f9b4136a`  
**Security patch branch:** `security/csrf-webhook-antiforgery-v23`  
**Prior patch commit:** `5d859e9`  
**Scope:** antiforgery boundary, Identity security-stamp revocation, operational account disablement, MFA enrollment lockout, HTTP integration-test harness.

## Local validation

- Locked dependency restore: PASS.
- Release solution build: PASS, 0 warnings and 0 errors.
- Domain checks: PASS, 15/15.
- HTTP test harness: compiles successfully; not run locally because the Windows computer has no usable disposable PostgreSQL 18 server.
- PostgreSQL/RLS/Identity integration: prior CI run on commit `5d859e9` passed all steps. The expanded workflow containing HTTP checks is pending a fresh CI run.

## Changes in this increment

### V3-01 — Immediate session revocation

**Change:** configured `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero` for the operational application cookie. Rotating an Identity security stamp is now checked on the next authenticated request rather than waiting for the default validation interval.

**Trade-off:** each authenticated request now performs a security-stamp validation against the Identity store. Measure latency and database load before production; if needed, design a distributed revocation cache without weakening immediate invalidation.

### V3-02 — Operational account disablement and session revocation service

Added `IOperationalAccountSecurityService` and its Identity-backed implementation:

- `RevokeSessionsAsync` rotates the security stamp and invalidates existing application cookies.
- `DisableAsync` enables lockout, sets the lockout end to `DateTimeOffset.MaxValue`, and rotates the security stamp.
- Missing identities return false; failed Identity operations throw an explicit internal exception.

**Boundary:** no administrative HTTP endpoint is exposed yet. Future endpoints must enforce Owner/Manager authority and audit the actor, target, reason, and outcome. Reactivation is intentionally not implemented.

### V3-03 — MFA enrollment hardening

- MFA setup sessions now reject locked/disabled operational identities.
- Enrollment verification refuses to proceed if MFA was already enabled in another session.
- Invalid enrollment codes increment Identity access-failure counters; reaching lockout signs out the short-lived MFA setup session.
- Successful enrollment resets the access-failure counter.

### V3-04 — HTTP integration harness

Added `tests/Atendee.Api.HttpChecks` using ASP.NET Core's in-memory test server and the disposable PostgreSQL database provisioned by CI. The harness covers:

- antiforgery bootstrap and secure cookie attributes;
- rejection of unsafe requests below a webhook-like path prefix without a request token;
- generic response equality for unknown identities and wrong passwords;
- successful cookie login and server-resolved tenant context;
- application cookie security attributes;
- immediate session revocation after security-stamp rotation;
- account disablement invalidating an existing cookie and preventing new login.

The workflow now runs this harness after the PostgreSQL persistence and Identity checks. CI must pass before this increment is considered validated.

## Existing security boundary retained

- The blanket `/api/v1/webhooks` antiforgery path exemption remains removed.
- All unsafe methods require antiforgery validation. A future webhook must implement raw-body signature verification, timestamp/replay checks, server-side provider-to-tenant mapping, idempotency, and a narrow endpoint-specific exception.
- Tenant membership remains resolved from the authenticated NameIdentifier through the restricted PostgreSQL function.
- Membership is re-resolved on authenticated requests; inactive membership is rejected.
- Owner/Manager requests require persisted MFA enablement and the signed `amr=mfa` marker.

## Open blockers

1. **Tenant status semantics require product approval.** The domain enum contains PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing, and Closed. Current login and middleware checks are not yet a single explicit deny-by-default authorization matrix. Do not guess billing grace periods or allowed read-only actions. Define which statuses may authenticate, use operational features, pay/resolve billing, export data, or close a tenant.
2. **Role/permission policies absent.** There are no business endpoints yet. Implement named policies and resource ownership checks before adding business routes; enforce the supplied permission matrix, including Professional's own appointments and customer data.
3. **Audit pipeline absent.** Account disablement, session revocation, MFA lifecycle, role changes, tenant status changes, and privileged actions need durable structured audit events. Do not expose the new account-security service through an endpoint until authorization and audit are present.
4. **HTTP test coverage is still partial.** Add MFA enrollment/challenge/recovery, lockout, logout, cookie renewal, CSRF on every unsafe endpoint, rate-limit behavior, membership deactivation, and tenant-status/role authorization cases.
5. **Rate limiting is process-local and keyed by remote IP.** Multi-instance production requires distributed enforcement, account/session-aware MFA throttling, and an explicit trusted-proxy/network configuration.
6. **Data Protection production key management remains unresolved.** Non-Windows production is intentionally denied until an approved certificate/KMS/managed protector and key backup/rotation/restore process exist.
7. **Platform Admin identity, MFA, break-glass, email provider, signup/invites/password reset, passkeys/WebAuthn, independent security review, and restore drill remain unimplemented.**

## Release and repository safety

- No change was made to `main`.
- No production deployment, real tenant data, signup, email delivery, or production secrets were used.
- No account-security HTTP endpoint has been exposed.
- No production readiness claim is made. Merge requires a reviewed PR and all release gates.
