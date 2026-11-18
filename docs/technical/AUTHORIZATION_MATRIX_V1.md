
# Authorization matrix — V1 (endpoints that exist today)

Status: describes only what is implemented and tested on branch `security/csrf-webhook-antiforgery-v23`. There are **no business endpoints yet**; this matrix covers the authentication surface and fixes the rule every future endpoint must follow. Not a production-readiness claim.

## Global rules (all endpoints)

- Deny by default. A route with no explicit policy is not allowed to expose tenant data.
- Every unsafe HTTP method (anything other than GET/HEAD/OPTIONS/TRACE) must pass antiforgery validation; failure returns 400. There is no path-based or webhook bypass.
- Tenant, role, status and actor are resolved server-side from the authenticated identity and the persisted membership on **every** request. Client-sent tenant, role, status or actor values are never consulted.
- On every authenticated request, in this order: security-stamp revalidation, active membership (else 401), tenant status policy for `Login` (else 403), MFA for Owner/Manager with a signed `amr=mfa` marker (else 403).
- Tenant status policy: `docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md`, code in `TenantAccessPolicy`.

## Existing endpoints

| Endpoint | Caller | Auth / policy | Rate limit | Expected outcomes |
|---|---|---|---|---|
| `GET /health/live` | anonymous | none | bootstrap-health | 200 |
| `GET /api/v1/auth/antiforgery` | anonymous | none | auth-antiforgery | 200 + token, `Cache-Control: no-store` |
| `POST /api/v1/auth/login` | anonymous | antiforgery | auth-login (5/window) | 200 authenticated; 202 `mfa_setup_required` or `mfa_required`; 401 generic for unknown user, wrong password, inactive membership or status policy denial |
| `POST /api/v1/auth/mfa/challenge` | pending-2FA cookie holder | antiforgery; membership active; status policy `Login` | auth-mfa (10/window) | 200 authenticated; 401 on bad or replayed code, lockout, inactive membership or policy denial |
| `POST /api/v1/auth/logout` | authenticated app session | `RequireAuthorization` | none | 204 |
| `GET /api/v1/auth/me` | authenticated app session, any of the four roles | membership active; status policy `Login`; MFA for Owner/Manager | none | 200 with server-resolved tenant, role and status; 401 no/revoked session or inactive membership; 403 policy denial or missing MFA |
| `POST /api/v1/auth/mfa/enrollment/start` | MFA-setup cookie (5 min) | policy `Atendee.MfaSetupOnly`; Owner/Manager only; status policy `Login` | auth-mfa | 200 secret and URI; 401 not eligible; 403 role or policy denial |
| `POST /api/v1/auth/mfa/enrollment/verify` | MFA-setup cookie | same as start | auth-mfa | 200 recovery codes shown once; 400 invalid code; 401 lockout; 403 role or policy denial |
| `POST /api/v1/auth/mfa/enrollment/cancel` | MFA-setup cookie | policy `Atendee.MfaSetupOnly` | auth-mfa | 204 |

## Role behavior on the authenticated surface

| Role | Login | MFA | `/me` |
|---|---|---|---|
| Owner | yes (status permitting) | mandatory (TOTP); setup session until enrolled | 200 only with `amr=mfa` |
| Manager | yes (status permitting) | mandatory; setup session until enrolled | 401 before MFA, 200 after |
| Receptionist | yes (status permitting) | not required by current policy | 200 |
| Professional | yes (status permitting) | not required by current policy | 200 |

Role changes and membership deactivation are re-evaluated on the next request: Owner to Receptionist shows the new role; Receptionist promoted to Owner without MFA gets 403; deactivated membership gets 401.

## Rule for every future business endpoint

1. Declare one `TenantCapability` (`ReadData`, `WriteData`, `Automation`, `ManageTeamAndSettings`, `Billing`, `ExportData`) and call `TenantAccessPolicy.IsAllowed(status, role, capability)` from the trusted request context.
2. Apply the fine-grained role permission from `docs/product/MATRIZ_PERMISSOES_V1.txt` after the status gate. The status gate only removes access; it never adds any.
3. Resolve the resource strictly inside the trusted tenant (RLS plus an application check); no tenant ID from the client.
4. Add tests: allowed role and status; each denied role; each denied status; cross-tenant attempt; mid-session role and status change; unauthenticated.
5. Add an audit event for sensitive actions (financial, export, permission change).
6. Never create admin disable/revoke, membership-management or cross-tenant endpoints before administrative authorization, MFA and auditing are designed and approved.

## Evidence

HTTP checks (`tests/Atendee.Api.HttpChecks`) cover: login and `/me` for each role; Manager and Owner blocked before MFA; promotion without MFA; demotion; deactivation; session revocation; and tenant-status behavior (Suspended allowed, Closing and SetupRequired denied for a receptionist, Closed and Closing ending a live session). Domain checks cover the exhaustive status by role by capability matrix and malformed inputs.
