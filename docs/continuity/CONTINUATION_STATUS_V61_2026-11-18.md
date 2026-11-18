# Atendee Beauty — Continuity Status V61 (2026-11-18)

## Scope
Close the V60 authorization-coverage gap for Manager and make the Professional service-price display independent of currency minor-unit conventions.

## Implemented
- Extended API HTTP integration coverage for the Manager role.
- Manager authentication is verified to remain blocked from protected API access until MFA enrollment is completed and verified.
- After MFA, Manager can discover eligible Professional memberships in the current tenant, create a Professional profile from an eligible membership, list the team, and read the current tenant's service catalogue.
- The Manager fixture provisions a dedicated Professional identity for that tenant. This avoids reusing an identity already associated with another tenant; the persistence fixture enforces the one-tenant identity relationship.
- Updated Professional self-service price presentation to show the service's stored minor-unit amount and label it as a minor unit, rather than assuming every currency has two fractional digits.
- No schema migration was introduced.

## Validation
- V61 implementation/fix commit: `a2169023009bf240e9ad4a3ca39fdd6be29305df`.
- Complete GitHub Actions workflow passed: run 44303068000.
- Job `Build and verify tenant isolation` completed successfully. All steps passed: frontend dependency audit, frontend tests, frontend build, locked .NET restore, Release build, domain checks, isolated PostgreSQL 18.7 provisioning, EF Core/PostgreSQL RLS integration checks, Identity/TOTP/tenant-context integration checks, and API HTTP authentication/session-security checks.
- Workflow: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44303068000
- V60 and V61 changes are validated on the working branch. Do not infer production deployment; none was performed.

## Repository and safeguards
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; `main` remains untouched.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI as source of truth.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- No deployment, SES configuration, billing/trial activation or production data changes are authorized.

## Next
1. Start V62: add real-browser E2E coverage for the critical team/professional flows and role boundaries (Owner, Manager, Receptionist and Professional).
2. Before choosing/adding browser tooling, inspect portal routing, dev-server/API integration, CI constraints and existing test conventions. The current `web/atendee-portal/package.json` has only build/test scripts and React/TypeScript/esbuild dev dependencies; Playwright is not currently declared there.
3. Prefer a small deterministic browser suite with isolated test data and no production services. Validate MFA and tenant boundaries without weakening existing API checks.
4. Keep all changes on this branch and require full CI success. Do not deploy.
