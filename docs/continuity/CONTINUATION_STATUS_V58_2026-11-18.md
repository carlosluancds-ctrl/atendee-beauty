# Atendee Beauty — Continuity Status V58 (2026-11-18)

## Scope
Service catalogue frontend completed and validated. No production deployment or real salon data was used.

## Frontend implementation
- Added React route `/services`; successful password/MFA authentication navigates to the catalogue.
- Loads tenant profile from `GET /api/v1/tenant` and service data from `GET /api/v1/services`.
- Owner/Manager see create, edit and archive controls.
- Receptionist sees read-only catalogue.
- Professional is not shown the unscoped catalogue until professional-service assignments exist.
- Create/edit sends explicit name, duration, integer minor-unit price, ISO 4217 currency, pre/post buffers and active state.
- Archive uses PUT with `isActive=false`; no DELETE.
- Mutations fetch a fresh antiforgery token and send X-CSRF-TOKEN with same-origin credentials and no-store.
- Logout uses the antiforgery-protected endpoint.
- Currency/price display intentionally states integer minor units plus ISO currency; no assumption of two decimal places.
- README updated.

## Validation
- Frontend implementation commit: `4af141c4eed1a9285c63f63769f49adcbb7942fd`.
- Full GitHub Actions run `44289289412` passed for that exact SHA:
  https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44289289412
- CI passed frontend npm ci, npm audit (0 vulnerabilities), Node tests, TypeScript/esbuild build, .NET locked restore/Release build, domain checks, PostgreSQL 18.7 provisioning, EF Core/RLS, Identity/TOTP and API HTTP integration checks.
- Local Windows Edge browser E2E passed against a temporary local mock API:
  - Owner: login redirect, list, create, edit and archive.
  - Receptionist: read-only list; editor and mutation controls absent.
  - Professional: unscoped catalogue blocked.
  - Six unsafe requests carried antiforgery header and cookie.
  - localStorage/sessionStorage remained empty.
- Browser E2E was a mock API test, not a real ASP.NET API through IIS. Temporary script remained under C:\Temp\atendee-work and was not committed.

## Current service catalogue scope
- Backend migration 013, RLS + FORCE RLS, restricted runtime grants, append-only audit trigger and GET/POST/PUT API were implemented in V52-V55.
- Backend exact SHA `66bd77a2eccdd5cb52ffafd4a6f808e19669864e` passed full CI run 44288089463.
- Technical spec: `docs/technical/SERVICE_CATALOGUE_V1.md`.
- No professional profiles/assignments, availability, appointments, pagination or currency conversion yet.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `security/csrf-webhook-antiforgery-v23`; `main` untouched.
- V57 frontend SHA: `4af141c4eed1a9285c63f63769f49adcbb7942fd`.
- V57 full CI run: 44289289412, success.
- V58 is a continuity-only checkpoint; its commit SHA is recorded in manifest.json.

## Next work
1. Backend-first professional profile and professional-service association.
2. Follow `MODELO_DADOS_MULTIEMPRESA_V1.txt` and `MATRIZ_PERMISSOES_V1.txt`: a professional sees only their own profile, assigned services and own agenda; no global service catalogue access.
3. Add tenant RLS, composite tenant foreign keys, role/ownership checks, audit and PostgreSQL integration tests before exposing endpoints.
4. Then implement professional UI and browser E2E.
5. Availability/appointments remain blocked until business hours, exception, buffer/conflict and DST rules are fully implemented.

## External production gates
SES sender/DNS/DKIM/SPF/DMARC and IAM; apply migrations through 013 and provision restricted roles; IIS same-origin /api reverse proxy and TLS; Platform Admin/MFA/break-glass; WebAuthn/passkey; non-Windows Data Protection; distributed rate limiting and trusted proxy. No production deployment authorized.
