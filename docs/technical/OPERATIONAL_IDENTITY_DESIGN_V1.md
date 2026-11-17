# Operational identity and trusted tenant context — v1

**Status:** implementation scaffold and disposable PostgreSQL integration checks in progress. This is not approval for production deployment.

## Boundaries

- Operational salon logins use ASP.NET Core Identity in the PostgreSQL `identity` schema.
- Platform administrator identities are not represented by `OperationalIdentityUser` and must use a separate store, cookie and authorization boundary in a later phase.
- One operational identity is constrained to one salon by `UNIQUE(identity_user_id)` in `atendee.tenant_users`.
- A globally unique normalized email prevents one email from resolving ambiguously across salons.
- Self-registration, email verification delivery, invitations and password-reset delivery remain disabled until an outbound mail provider and operational process are selected.

## Login and MFA

- Password verification and account lockout use ASP.NET Core Identity; no custom password hash is implemented.
- New identities require confirmed email. Lockout is configured for five failed password attempts and a 15-minute lockout; the API also has per-IP fixed-window rate limits.
- Owner and Manager accounts are required to enroll TOTP before obtaining the normal application cookie. MFA challenge supports TOTP or a one-time recovery code.
- Recovery codes are generated once and returned only in the no-store enrollment response. Identity's stored token values are encrypted through an EF Core value converter backed by ASP.NET Core Data Protection.
- The custom TOTP provider accepts a 30-second RFC 6238 time step with one adjacent step of drift. It uses a separate `identity.TotpReplayGuards` row and an atomic conditional update so the same time step cannot be accepted twice, including under concurrent requests. The guard is outside the Identity user row so profile/security-stamp updates cannot roll it back.
- Server clocks must use reliable time synchronization. A future security review should test clock drift, lockout and MFA recovery procedures.

## Tenant resolution

- The API cookie's signed `NameIdentifier` is the source of the operational identity ID.
- `PostgresTenantMembershipResolver` opens a short transaction, sets `app.identity_user_id` transaction-locally from that authenticated identity, and calls the fixed `SECURITY DEFINER` function `atendee.resolve_tenant_membership()`.
- The function accepts no user or tenant ID argument. The database role `atendee_auth_login` has no direct SELECT privileges on `atendee.tenant_users` or `atendee.tenants`.
- PostgreSQL clears the transaction-local GUC on commit/rollback before the physical connection returns to the pool.
- HTTP headers, route values, query strings and body fields never set the trusted tenant context. Claims containing tenant ID/role are replaced on each normal authenticated request with values from the server-side membership.

**Trust-boundary limitation:** PostgreSQL custom GUCs can be set by a database session. The transaction-local identity value is trusted only because the backend derives it from the authenticated cookie and uses parameterized SQL. It does not protect against arbitrary SQL execution, SQL injection, or compromise of the shared database credential. Continue to prohibit arbitrary SQL, keep every query parameterized, restrict credentials, and test cross-tenant access. RLS is a second barrier, not a substitute for application authorization.

## Cookies, CSRF and data protection

- Normal authentication uses a Secure, HttpOnly, SameSite=Strict `__Host-` cookie with a 30-minute sliding expiry.
- MFA setup uses a separate, short-lived five-minute cookie; it is not the normal application session.
- Unsafe HTTP methods require an antiforgery token in `X-CSRF-TOKEN`.
- Auth endpoints are rate limited per client IP; Identity also enforces user-level lockout.
- Outside Development, startup fails unless both Identity and tenant connection strings and a Data Protection key-ring directory are configured. On Windows, keys are protected using DPAPI for the current service user. The service identity must remain stable; the directory needs strict ACLs and encrypted, tested backup/restore. Non-Windows production currently fails closed until an explicit certificate or managed key protector is implemented.

## Identity schema

- ASP.NET Core Identity schema version 2 is selected explicitly so the manual migration matches the EF model. Passkey/WebAuthn support is deferred to a reviewed additive migration.
- Migration `002_create_operational_identity.sql` creates only the operational identity tables, tenant membership, and the restricted membership function. It must run as the dedicated migrator against a newly provisioned database.
- `LastTotpAcceptedStep` is a nullable bigint on `identity.AspNetUsers`, updated atomically by the custom token provider.

## Remaining work before any production identity rollout

1. Run the complete PostgreSQL 18 CI workflow with the new migration and require every identity check to pass without SKIP.
2. Add API-level tests for cookie sign-in, MFA setup/challenge, CSRF, rate limiting, session revocation, account disablement and enumeration.
3. Add password reset, email verification and tenant-bound invitation tokens only after selecting a mail provider; store only token hashes and never log tokens.
4. Implement separate Platform Admin identity/MFA and audited break-glass.
5. Implement authorization policies per role and tenant state before adding customer, catalog, scheduling, conversations or billing endpoints.
6. Add audit events, security event monitoring, key rotation and restore drills.
7. Complete the full product security acceptance plan and independent security review.

The current code is not production-ready and must not be deployed or used with real salon data yet.
