# Atendee Beauty — Continuity Status V71 (approved, 2026-11-19)

## Result

V71 approved after the complete PostgreSQL 18 persistence integration workflow passed on commit `63ecaa52f8cab3699990bd767d84233717fa755e`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow run: [44458718974](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44458718974)
- Attempt: 1
- Job: `Build and verify tenant isolation`
- Job conclusion: `success`

## Scope V71 — pending-MFA audit failure cannot leave a challenge session

Added a real HTTP integration regression in `tests/Atendee.Api.HttpChecks/Program.cs`. The test creates a synthetic MFA-enabled identity, enables a PostgreSQL trigger that rejects only `operational_auth.password_verified_mfa_pending`, and performs a password login through Kestrel's test host.

Verified assertions from the CI log:

1. Mandatory pending-MFA audit failure aborts the login request with HTTP 500.
2. The rejected pending-MFA audit event is not persisted.
3. The temporary two-factor cookie is not usable after the failed request, even when a valid recovery code is submitted.
4. The failed request cannot be upgraded to an authenticated application session.
5. The test trigger and function are removed after the request, including when the request fails.

The first CI attempt on `e34dd36632cb51a05cc7b75d2db55d196fb51826` failed at Release compilation because a local `recoveryCodes` name shadowed an enclosing local. This was corrected by renaming the test local to `auditFailureRecoveryCodes`; the corrected SHA above passed the complete workflow.

## Complete CI evidence — run 44458718974

- Frontend dependency audit, frontend tests and build: success.
- Portal browser E2E: 8 passed (6.7s).
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- New V71 audit-failure assertions: all passed, explicitly present in the job log.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed (6.0s).
- Disposable database credentials cleared and PostgreSQL container removed.

## Commits

- `e34dd36632cb51a05cc7b75d2db55d196fb51826` — initial V71 test (first CI failed at compile).
- `63ecaa52f8cab3699990bd767d84233717fa755e` — fix test local variable scope; complete CI passed.

## Historical checkpoints preserved

V64 antiforgery session transitions, V65 bidirectional service isolation, V66 professional identity/profile isolation, V67 cross-tenant service assignment rejection without partial mutation, V68 trusted-context spoofing resistance, V69 onboarding actor and TOTP audit atomicity, and V70 failed-password counter/audit atomicity remain in the passing CI flow. Previous continuity files are unchanged.

## Next step

Review the login lockout snapshot and audit-volume policy around `alreadyLockedOut` in `OperationalAuthenticationEndpoints.LoginAsync`. Establish the exact sequential and concurrent behavior at the configured five-attempt threshold. Add negative/positive HTTP regression coverage only where needed, preserving the rule that the threshold-causing failed attempt is audited, while subsequent attempts against an already-locked account do not create unbounded audit rows. Do not weaken lockout, change thresholds, or suppress legitimate audit events.

## Guardrails

- `main` remains untouched.
- No deployment or production data changes.
- PostgreSQL CI database is disposable and removed at job completion.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
