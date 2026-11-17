# Security boundaries in the Atendee starter kit

**Status:** operational identity and trusted tenant-context scaffolding are implemented locally; full API/security acceptance remains incomplete. Do not deploy publicly.

- Operational identity is separate from the future Platform Admin identity; the latter is not yet implemented.
- ASP.NET Core Identity owns password hashing, lockout and MFA token management. No custom password hasher is present.
- Identity schema v2 is selected explicitly; WebAuthn/passkeys are deferred to a reviewed additive migration.
- Owner and Manager login requires TOTP enrollment before the normal application cookie is issued. TOTP values and recovery codes are protected at rest by Data Protection; the custom provider enforces one successful use per TOTP time step.
- Identity's normal and MFA-setup cookies are Secure, HttpOnly, SameSite=Strict and use the `__Host-` prefix. Unsafe methods require antiforgery validation.
- Membership is resolved on each authenticated request from the signed identity subject and a server-side PostgreSQL membership record. Client tenant IDs are never treated as authoritative.
- The auth database role has no direct SELECT rights on tenant memberships; it uses a fixed SECURITY DEFINER function and a transaction-local identity context.
- A custom PostgreSQL GUC can be set by any SQL session. This backend-derived GUC is not SQL-injection protection; arbitrary SQL must remain impossible and all database inputs parameterized.
- No public registration, email verification, invitation, or password-reset delivery is enabled. No outbound email provider has been selected.
- No platform administrator store, role-specific business authorization endpoints, audit event pipeline, password-reset workflow, session revocation UI, or production deployment configuration has been completed.
- No business endpoints are present yet. Before adding them, implement status-aware tenant policies: `pending_verification`, `setup_required`, `trial`, `active`, `past_due`, `suspended`, `closing`, and `closed`. Suspended tenants may need narrowly scoped billing/data-export access, not ordinary operations.
- Authentication rate limits currently use in-memory fixed-window partitions by remote IP. Before deployment behind a proxy or multiple API instances, configure trusted forwarded headers and a shared/distributed rate limiter.
- Do not enable CORS until exact frontend origins are known.
- Production Data Protection requires a persistent key ring and an explicit protector. Current Windows path uses DPAPI for the service account; maintain stable service identity, strict ACLs and encrypted tested backups. Non-Windows deployment fails closed until certificate/managed protection is configured.
- Do not add production secrets to configuration files.
- The SQL files under `db/poc` are disposable lab scripts, not production migrations.

The health endpoint is a scaffold convenience, not a production readiness check. A green persistence or identity CI job does not prove complete application security or authorize deployment.
