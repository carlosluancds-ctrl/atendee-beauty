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
- The platform-administrator identity realm, administrative disable/revoke endpoints, password-reset workflow, session-revocation UI and production deployment configuration have not been completed. A mandatory operational-identity audit ledger and tenant business audit events exist; neither is an immutable ledger against a database owner/superuser.
- Current business endpoints cover tenant services, professionals and onboarding. Extend the approved status-aware tenant policy to every new endpoint: `pending_verification`, `setup_required`, `trial`, `active`, `past_due`, `suspended`, `closing`, and `closed`. Suspended tenants may need narrowly scoped billing/data-export access, not ordinary operations.
- Authentication rate limits use in-memory fixed-window partitions by the effective remote IP. Forwarded headers are accepted only from exact IPs listed in `ForwardedHeaders:KnownProxies`; the configured allowlist defaults empty and is enforced as deny-all with unmatchable unspecified-IP sentinels (an empty ASP.NET known-proxy collection alone would disable the address check). The allowlist parser canonicalizes IPv4-mapped IPv6 values before validation/storage and rejects unspecified, broadcast, IPv4 multicast/reserved ranges (first octet 224–255), and IPv6 multicast addresses. The forwarding limit is one hop, header symmetry is required, and only X-Forwarded-For/X-Forwarded-Proto are enabled. X-Forwarded-Host is never trusted. Configure actual proxy IPs through deployment environment settings; no proxy address is assumed or committed. The limiter remains process-local/in-memory, so multiple API instances still require an approved shared/distributed limiter before deployment.
- Do not enable CORS until exact frontend origins are known.
- Production Data Protection requires a persistent `DataProtection:KeyRingPath` and an explicit protector. Windows uses DPAPI by default when no certificate is configured; a configured RSA PKCS#12/PFX certificate (`DataProtection:CertificatePath` plus `DataProtection:CertificatePassword`) can protect the persistent key ring on Windows or non-Windows. The certificate must be currently valid and include RSA public/private keys; startup fails closed on invalid configuration. Supply the PFX and password only through a deployment secret manager or protected mount, never appsettings/source control. Keep the key ring on persistent storage with restrictive service-account ACLs/permissions; all replicas share the same ring, application name and active certificate. Back up the encrypted ring together with its matching private certificate and test restoration. Current code supports one active certificate only: do not rotate/replace it blindly because old keys still require the certificate that encrypted them; a coordinated rotation/decryption procedure remains an explicit deployment prerequisite. No production certificate or key-ring path has been configured.
- Do not add production secrets to configuration files.
- The SQL files under `db/poc` are disposable lab scripts, not production migrations.

The health endpoint is a scaffold convenience, not a production readiness check. A green persistence or identity CI job does not prove complete application security or authorize deployment.

## Tenant customer records (V83)

- `atendee.customers` is tenant-scoped with a composite `(tenant_id, customer_id)` primary key, PostgreSQL RLS enabled and forced, and a runtime policy bound only to the transaction-scoped `app.tenant_id`.
- The runtime role can SELECT/INSERT/UPDATE only through the approved tenant transaction path; it has no DELETE or TRUNCATE grant. Customer mutations require trusted `app.actor_id` and append a tenant audit event in the same transaction.
- API read/write is limited to Owner, Manager and Receptionist subject to persisted tenant capabilities/status. Archive is limited to Owner and Manager. Professional does not receive general customer-directory access.
- Customer list is bounded and paginated (default 50, maximum 100). Contact fields are minimized to display name and optional normalized phone/email; at least one contact is required. Phone values are normalized to E.164 syntax but are not claimed to be verified or deliverable.
- Shared phone numbers are allowed; no unique contact constraint or automatic merge was added. A duplicate-resolution policy remains open.
- A phone/email on a customer record is not marketing consent. No WhatsApp/email campaign, free-form notes, consent inference, physical deletion or retention policy was added. Consent, contact verification, export/erasure and retention require separately reviewed workflows.

## Tenant business-hours configuration (V84)

- `atendee.business_hours` stores recurring weekly intervals as local wall-clock `TIME WITHOUT TIME ZONE` values. The API always returns the tenant's configured time-zone ID with the schedule; clients must not interpret these values as UTC.
- ISO weekday numbering is Monday=1 through Sunday=7. Empty schedule means closed. The domain and database enforce at most four intervals per day, at most 28 per week, start-before-end, same-day intervals and no overlaps. Overnight spans must be represented as two day intervals; no implicit overnight conversion is performed.
- Owner/Manager may replace the schedule; Receptionist may read; Professional has no salon-wide business-hours endpoint access. All writes are tenant-scoped, antiforgery-protected and audited.
- Full replacements acquire a transaction-scoped advisory lock derived from tenant ID. A deferred constraint trigger independently serializes and rejects overlapping intervals/max-count violations, including direct SQL and concurrent writes. Removed intervals are soft-archived; runtime has no DELETE/TRUNCATE grant. No default hours are inferred.
- This increment does not calculate appointment availability or create bookings. Date-specific time-zone conversion, daylight-saving ambiguous/nonexistent local-time policy and conversion to UTC appointment instants must be implemented and tested in the availability/booking layer before booking endpoints exist.
- The tenant transaction runner preserves the original database exception when a deferred constraint fails at COMMIT and Npgsql has already marked the transaction complete; rollback cleanup must not mask the database failure.
