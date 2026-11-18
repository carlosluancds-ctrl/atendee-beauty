# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Before changing code:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23. Never modify main.
- Latest implementation commit: 66bd77a2eccdd5cb52ffafd4a6f808e19669864e.
- Full CI run 44288089463 passed for that exact SHA.
- Latest continuity checkpoint is V56; its own commit SHA is recorded in manifest.json.

## Completed
- V41 offline first-Owner salon provisioning command.
- V42/V44 separate offline operational identity create/reissue, SES v2 sender, activation API, restricted provisioner DB login and migration 011.
- V45 activation page; V47 login, TOTP/recovery challenge, MFA setup and one-time recovery-code UI.
- V49 Edge browser review with local mock API passed activation and MFA paths; real API browser E2E remains untested.
- V50 authenticated GET /api/v1/tenant with minimal salon profile projection; migration 012.
- V52-V55 service catalogue backend, migration 013, tests and fixes.
- V50 implementation CI: run 44285681344 passed for 10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd.
- Service catalogue final CI: run 44288089463 passed for 66bd77a2eccdd5cb52ffafd4a6f808e19669864e.

## Service catalogue backend
- Domain validates trimmed name (max 120), duration 1-1440 minutes, integer nonnegative minor-unit price, explicit ISO currency, buffers 0-1440, UTC timestamps.
- Table atendee.services: composite tenant/service key, tenant-first index, RLS + FORCE RLS.
- Runtime can SELECT/INSERT/UPDATE; DELETE/TRUNCATE revoked.
- atendee.tenant_audit_events is append-only to runtime; trigger logs create/update/archive with trusted actor context.
- API: GET /api/v1/services, GET /{id}, POST, PUT /{id}.
- Owner/Manager manage when WriteData allowed. Owner/Manager/Receptionist read when ReadData allowed. Professional denied until associations exist.
- No client-supplied tenant_id. Foreign UUID hidden by PostgreSQL RLS.
- Technical spec: docs/technical/SERVICE_CATALOGUE_V1.md.
- Frontend UI still pending.

## Architecture invariants
- No public self-registration, public tenant creation or public first-Owner endpoint.
- Activation token: 256-bit random, one-time, 30-minute expiry, SHA-256 hash only; atomic activation, no session.
- First Owner enrolls MFA before V41 offline salon provisioning.
- Tenant ID/role/status derive only from server-side membership.
- App/auth logins have no direct SELECT on tenant tables; use narrow SECURITY DEFINER projection/functions.
- Owner/Manager require MFA and signed amr=mfa marker.
- Unsafe HTTP requests require antiforgery; secure HttpOnly cookies; no auth/CSRF tokens in localStorage/sessionStorage.
- Tenant lifecycle transition remains blocked until contact verification, approved terms version/content, essential data and transition authority are defined.

## Validation
- Latest service implementation full CI: run 44288089463 success, exact SHA 66bd77a2eccdd5cb52ffafd4a6f808e19669864e.
- Frontend npm ci/audit/tests/build, .NET locked restore/Release build/domain checks, PostgreSQL 18.7 migrations, EF Core/RLS, Identity/TOTP and API HTTP tests all passed.
- Service tests include create/update/archive audit, role restrictions, integer currency handling and foreign-tenant RLS.
- Browser-level review used a mock API, not real ASP.NET API.
- No production email, DNS, AWS, server change, production data or deployment.

## Next
1. React/TypeScript service catalogue UI: list/create/edit/archive using the tested API, with explicit currency and minor-unit handling.
2. Browser E2E for Owner manage, Receptionist read-only, and Professional denied.
3. Professional profile + professional-service association backend before availability or agenda.
4. Do not invent defaults, pricing rules, taxes, commercial terms or onboarding terms.

## External production gates
SES sender/DNS/DKIM/SPF/DMARC, SES production access and IAM; apply migrations through 013 and provision roles; IIS same-origin /api proxy and TLS; Platform Admin/MFA/break-glass; WebAuthn/passkey; non-Windows Data Protection; distributed rate limits and trusted proxy. No production deployment authorized.

## Continuity rule
After every increment, write a new versioned status file, update this index and manifest, commit directly to this branch, verify exact SHA and files, preserve prior versions, and record only observed tests/CI.
