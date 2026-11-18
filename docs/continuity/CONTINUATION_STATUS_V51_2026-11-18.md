# Atendee Beauty — Continuity Status V51 (2026-11-18)

## Scope
V50 implements read-only current-salon profile retrieval. No tenant lifecycle transition, terms acceptance, public registration, production configuration or deployment was added.

## Implementation
- Added authenticated `GET /api/v1/tenant`.
- Response contains tenant UUID, name, slug, persisted time-zone ID, lifecycle status, and the caller's persisted membership display name and role.
- Tenant identity is resolved from the authenticated server-side membership; no tenant ID from URL, query, header or body is accepted.
- Extended the narrow `atendee.resolve_tenant_membership()` SECURITY DEFINER projection to return tenant name, slug and time-zone ID.
- Added migration `db/migrations/012_tenant_profile_projection.sql`.
- The auth login still has no direct SELECT on `atendee.tenants` or `atendee.tenant_users`; the projection function is the only read path.
- Extended trusted request context and membership snapshot with the three tenant profile fields.
- Added HTTP checks for anonymous denial, authenticated profile response, persisted role/status and the absence of direct SELECT privilege on tenants.
- Added migration 012 to the PostgreSQL 18.7 CI workflow.

## Validation
- Commit: `10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd`.
- Full GitHub Actions run `44285681344` completed successfully for that exact commit.
- Passed: frontend npm ci/audit/tests/build; locked .NET restore; Release build; domain checks; disposable PostgreSQL 18.7 provisioning; EF Core/RLS persistence checks; Identity/TOTP/tenant-context checks; API HTTP authentication, antiforgery, cookie, session and new tenant-profile checks.
- No production database, email provider, AWS account, DNS or server was used.

## Product-policy boundary
The approved lifecycle policy explicitly leaves authority and process for changing tenant status outside its scope. The onboarding specification also requires contact verification, terms acceptance and essential salon data before `SetupRequired`. Therefore V50 deliberately does not expose any endpoint that changes tenant status or accepts terms. Do not implement that transition until the owner approves the terms version/content and the corresponding onboarding data/authority contract.

## Current branch
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `security/csrf-webhook-antiforgery-v23`; `main` untouched.
- V50 implementation SHA: `10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd`.
- V50 CI: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44285681344
- V51 is this continuity-only checkpoint. Preserve all prior versioned status files.

## Next work
1. Continue backend-first through the approved catalogue/service vertical slice, using `MODELO_DADOS_MULTIEMPRESA_V1.txt`, `MATRIZ_PERMISSOES_V1.txt` and backlog acceptance criteria as constraints.
2. Define service schema, tenant RLS, validation, permissions and audit before exposing CRUD endpoints.
3. Do not assume currency, default duration, tax behavior or commercial terms beyond explicitly documented requirements.
4. Salon setup status transition remains blocked on the missing terms and onboarding acceptance contract.

## External production gates still pending
- SES sender domain/address, DKIM, SPF/DMARC, production access and IAM.
- Apply migrations through 012 and provision restricted DB roles in a target environment.
- IIS same-origin /api reverse proxy and TLS.
- Platform Admin identity/MFA/break-glass; WebAuthn/passkey.
- Non-Windows Data Protection key protection, distributed rate limiting and trusted proxy configuration.
- Salon dashboard, service catalogue, professionals, customers, agenda, billing and other business workflows.
- No production deployment authorized or performed.
