# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Before changing code:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23. Never modify main.
- Latest implementation SHA: 10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd.
- Full CI run 44285681344 passed for that exact SHA.
- Latest continuity checkpoint is V51; the commit for it is recorded in manifest.json.

## Completed milestones
- V41 offline first-Owner salon provisioning command; remains the only first-Owner membership creation path.
- V42/V44 separate offline operational identity create/reissue, SES v2 mailer, activation API, restricted provisioner DB login and migration 011.
- V43 documented SES v2 architecture.
- V45 activation page.
- V47 login, TOTP/recovery challenge, MFA setup and one-time recovery-code experience.
- V49 browser review using Edge + local mock API passed activation, pending-owner MFA setup, TOTP and recovery-code flows. This is not real API browser E2E.
- V50 authenticated read-only GET /api/v1/tenant; migration 012 extends the SECURITY DEFINER membership projection with salon name, slug and timezone. The auth DB role retains no direct SELECT on tenants or tenant_users.
- V50 exact implementation SHA 10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd passed full CI run 44285681344.

## Architecture invariants
- No public self-registration, public tenant creation or public first-Owner endpoint.
- Pending identity activation is single-use, 256-bit random token, 30-minute expiry, SHA-256 hash only; activation confirms email and sets password atomically, without issuing a session.
- Pending first Owner must sign in and enroll MFA before V41 offline provisioning creates salon and Owner membership.
- Tenant ID, role and status come only from the server-resolved membership.
- App/auth logins do not directly read tenant tables; use narrow SECURITY DEFINER projections/functions.
- Owner/Manager require TOTP/recovery MFA and signed amr=mfa marker on each authenticated request.
- Unsafe HTTP requests require antiforgery; secure HttpOnly same-origin cookies; no auth/CSRF tokens in localStorage/sessionStorage.
- V50 only reads tenant profile; no lifecycle transition endpoint.

## Validation
- Latest full CI: run 44285681344, success, exact SHA 10ccc184fcd094a9ae6aa33ca90161cc90a2fcfd.
- Frontend npm ci/audit/tests/build passed.
- .NET locked restore, Release build and domain checks passed.
- Disposable PostgreSQL 18.7, EF Core/RLS, Identity/TOTP and HTTP authentication/session checks passed, including new migration 012 and tenant profile tests.
- Browser E2E remains limited to local mock API; real ASP.NET API through production-style IIS proxy remains untested.
- No production email, DNS, AWS, server changes, production data or deployment.

## Current product-policy boundary
The approved lifecycle policy leaves authority/process for tenant status changes out of scope. The product identity specification requires contact verification, terms acceptance and essential salon data before SetupRequired. Do not implement a status transition until terms content/version and the onboarding data/authority contract are approved.

## Next implementation direction
Backend-first service catalogue vertical slice. First inspect MODELO_DADOS_MULTIEMPRESA_V1.txt, MATRIZ_PERMISSOES_V1.txt and backlog. Define tenant schema, RLS, validation, permissions, audit and tests before CRUD. Do not invent currency, duration defaults, tax treatment or commercial terms.

## External production gates
- SES sender identity/DNS/DKIM/SPF/DMARC, production access and IAM.
- Apply migrations through 012 and provision restricted roles in target environment.
- IIS same-origin /api reverse proxy and TLS.
- Platform Admin identity/MFA/break-glass; WebAuthn/passkey.
- Non-Windows Data Protection protection, distributed rate limiting, trusted proxy.
- Salon dashboard and business workflows remain incomplete.
- No production deployment authorized.

## Continuity rule
After every completed increment, write a new versioned status file, update this index and manifest, commit directly to this branch, verify exact SHA and files, and preserve all earlier status versions. Report only observed tests/CI.
