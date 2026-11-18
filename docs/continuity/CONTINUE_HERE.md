# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Before changing code:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23. Never modify main.
- Latest frontend implementation SHA: 4af141c4eed1a9285c63f63769f49adcbb7942fd.
- Full CI run 44289289412 passed for that exact SHA.
- Latest continuity checkpoint is V58; its own commit SHA is recorded in manifest.json.

## Completed
- V41 offline first-Owner salon provisioning command.
- V42/V44 separate offline operational identity create/reissue, SES v2 sender, activation API, restricted provisioner DB login and migration 011.
- V45 activation page; V47 login, TOTP/recovery challenge, MFA setup and one-time recovery-code UI.
- V49 Edge browser review with local mock API passed activation/MFA paths; real API browser E2E remains untested.
- V50 authenticated GET /api/v1/tenant with minimal salon profile projection; migration 012.
- V52-V55 service catalogue backend, migration 013, audit, RLS, role/status checks and full integration tests.
- V56 service catalogue docs and continuity.
- V57 React /services UI, role-aware controls, same-origin antiforgery mutations and login redirect.
- Service backend CI: run 44288089463 success for 66bd77a2eccdd5cb52ffafd4a6f808e19669864e.
- Frontend CI: run 44289289412 success for 4af141c4eed1a9285c63f63769f49adcbb7942fd.

## Service catalogue
- Domain validates name, duration 1-1440, integer nonnegative minor-unit price, explicit ISO currency, buffers 0-1440 and UTC timestamps.
- Table atendee.services: composite tenant/service key, tenant-first index, RLS + FORCE RLS.
- Runtime SELECT/INSERT/UPDATE only; DELETE/TRUNCATE revoked.
- Append-only tenant audit events and trigger for service.created/updated/archived.
- API GET/POST/PUT; Owner/Manager manage; Owner/Manager/Receptionist read subject to tenant status; Professional denied until assignments exist.
- UI route /services: Owner/Manager create/edit/archive, Receptionist read-only, Professional blocked from unscoped catalogue.
- Mutations use antiforgery token and cookie; no auth/CSRF material in local/session storage.
- Browser E2E against mock API passed Owner create/edit/archive, Receptionist read-only, Professional denial; 6 unsafe calls carried CSRF header/cookie.
- Real ASP.NET API browser E2E through IIS remains untested.

## Architecture invariants
- No public self-registration, public tenant creation or public first-Owner endpoint.
- Activation token: 256-bit random, one-time, 30-minute expiry, SHA-256 hash only; atomic activation, no session.
- First Owner enrolls MFA before V41 offline salon provisioning.
- Tenant ID/role/status derive only from server-side membership.
- App/auth logins have no direct SELECT on tenant tables; use narrow SECURITY DEFINER projection/functions.
- Owner/Manager require MFA and signed amr=mfa marker.
- Unsafe HTTP requests require antiforgery; secure HttpOnly cookies.
- Tenant lifecycle transition remains blocked until contact verification, approved terms version/content, essential data and transition authority are defined.

## Next
1. Backend-first professional profile and professional-service assignment model.
2. Enforce Professional only sees own profile, assigned services and own agenda; no global catalogue.
3. Tenant RLS, composite FKs, ownership checks, audit and PostgreSQL tests before API/UI.
4. Then professional UI and browser E2E.
5. Availability/appointments after business hours, exceptions, buffers, conflict and DST rules are specified and tested.

## External production gates
SES sender/DNS/DKIM/SPF/DMARC and IAM; apply migrations through 013 and provision roles; IIS same-origin /api proxy and TLS; Platform Admin/MFA/break-glass; WebAuthn/passkey; non-Windows Data Protection; distributed rate limits and trusted proxy. No production deployment authorized.

## Continuity rule
After every increment, write a new versioned status file, update this index and manifest, commit directly to this branch, verify exact SHA and files, preserve prior versions, and record only observed tests/CI.
