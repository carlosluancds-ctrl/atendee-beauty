# Atendee Beauty — Continuity Status V56 (2026-11-18)

## Scope
Backend service catalogue vertical slice completed and full CI verified. This increment adds no frontend UI, no professional association, no appointment scheduling and no production deployment.

## Implementation
- Domain entity `Atendee.Domain.Catalog.SalonService` validates name, duration, price, ISO currency, buffers and UTC timestamps.
- Duration: integer 1-1440 minutes.
- Price: non-negative Int64 minor units; currency required explicitly and validated against runtime region ISO currency data. No floating point and no default currency.
- Buffers: integer 0-1440 minutes before and after.
- Active/inactive lifecycle uses `is_active`; no hard delete endpoint.
- Added `atendee.services`, composite PK `(tenant_id, service_id)`, tenant-first index, RLS + FORCE RLS, and runtime SELECT/INSERT/UPDATE only. DELETE/TRUNCATE are revoked.
- Added append-only `atendee.tenant_audit_events`; SECURITY DEFINER trigger records `service.created`, `service.updated` and `service.archived` using the transaction-local actor context. Missing actor context aborts the mutation.
- Added migration `db/migrations/013_create_tenant_services.sql`.
- Added tenant-scoped EF mapping, repository and application abstraction. All reads/writes execute through `ITenantTransactionRunner`.
- Added API:
  - `GET /api/v1/services`
  - `GET /api/v1/services/{id}`
  - `POST /api/v1/services`
  - `PUT /api/v1/services/{id}`
- Owner/Manager can manage services only when tenant status allows WriteData. Owner/Manager/Receptionist can read only when status allows ReadData. Professional is deliberately denied until professional-service association exists.
- Tenant ID is never accepted from HTTP input. Foreign UUID visibility is blocked by PostgreSQL RLS.
- Updated API contract and master backlog; added `docs/technical/SERVICE_CATALOGUE_V1.md`.

## Tests and CI
- Final implementation commit: `66bd77a2eccdd5cb52ffafd4a6f808e19669864e`.
- Full GitHub Actions run `44288089463` completed successfully for that exact SHA:
  https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44288089463
- Passed frontend npm ci/audit/tests/build, locked .NET restore, Release build, domain checks, PostgreSQL 18.7 migration provisioning, EF Core/RLS checks, Identity/TOTP checks and API HTTP integration checks.
- Domain checks cover normalization, currency, negative price, duration, buffers and timestamp invariants.
- HTTP checks cover receptionist read vs write denial, Owner create/update/archive, tenant RLS with a known foreign UUID, integer money/currency preservation and database audit events.
- Two intermediate CI attempts failed during development (one unsupported .NET API call; one incorrect audit-test expectation). Both were corrected; final full run above passed.

## Known limits
- No frontend service catalogue UI yet.
- No professional profiles or professional-service associations.
- No pagination/filter contract yet.
- No appointment or availability integration.
- Currency minor-unit exponents are not used for display/conversion logic; API stores integer minor units and explicit ISO code only.
- No default duration, buffer, currency, tax, discount or commercial terms have been introduced.

## Current branch
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `security/csrf-webhook-antiforgery-v23`; `main` untouched.
- Implementation SHA: `66bd77a2eccdd5cb52ffafd4a6f808e19669864e`.
- V56 continuity-only checkpoint will follow this implementation commit. Preserve prior versioned statuses.

## Next work
1. Build the React/TypeScript service catalogue UI against the tested API: list, create, edit and archive, with explicit currency/minor-unit handling and no inferred defaults.
2. Add browser E2E for Owner manage and Receptionist read-only states; Professional must not see the unscoped catalogue.
3. Then proceed to professional profile and service association domain, using backlog/model contracts before any appointment availability work.
4. Keep onboarding status transition blocked until terms/version, essential salon data and authority process are approved.

## External production gates
SES/DNS/IAM, apply migrations through 013 and provision restricted roles, IIS same-origin /api reverse proxy and TLS, Platform Admin/MFA/break-glass, WebAuthn/passkey, non-Windows Data Protection, distributed rate limiting and trusted proxy remain pending. No production deployment authorized or performed.
