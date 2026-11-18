# Atendee Beauty - Continuity Status V41 (2026-11-18)

## Scope

Implement the owner-approved offline operator command for creating a salon and attaching its first Owner after that identity has completed MFA enrollment. No public signup, API endpoint, Platform Admin, audit read/export endpoint, tenant-status mutation flow, migration, or deployment was introduced.

## Owner approval and product boundaries

- On 2026-11-18, the owner selected option 1: an internal operator tool/command, not a public or authenticated HTTP endpoint.
- The command provisions only an already-existing identity. It does not create a login identity, password, email verification, TOTP secret, or recovery codes.
- The first identity must already have confirmed email and complete the V40 pre-membership MFA enrollment flow before this command can create Owner membership.
- No new decision was made about tenant lifecycle, status changes, audit visibility/retention, post-Closed handling, Platform Admin, or public signup.
- The tenant is created in the domain-default PendingVerification status. The command never changes tenant status.

## Verified starting state

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23
- Code commit: 37300e50e2c27c70ac30d820fe83034c71ae4b45
- Remote branch SHA was verified equal to the code commit using git ls-remote.
- CI run 44234622468 completed success for that exact SHA: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44234622468
- main was not changed. No deployment was performed.

## V41 implementation

- Added src/Atendee.Infrastructure/Identity/FirstOwnerProvisioningService.cs.
- Added tools/Atendee.FirstOwnerProvisioning as a standalone .NET console project and included it in Atendee.sln with a locked dependency file.
- Added docs/operations/FIRST_OWNER_PROVISIONING.md as the operator runbook.
- The service is deliberately not registered in the API dependency-injection container and no HTTP route was added.
- The command accepts an existing identity GUID, salon name, unique slug, time-zone ID, and Owner display name.
- The tool requires the database session_user to be exactly atendee_migrator. It rejects application login, auth login, postgres, and superuser sessions.
- The dedicated atendee_migrator role is NOINHERIT, non-superuser and non-BYPASSRLS; it uses transaction-local SET ROLE to the existing atendee_identity_owner and atendee_owner NOLOGIN roles.
- Before inserting anything, it verifies the identity exists, EmailConfirmed is true, TwoFactorEnabled is true, LockoutEnd is not in the future, and no salon membership already exists.
- A transaction-scoped advisory lock serializes concurrent invocations for the same identity. The database unique membership constraint remains the final guard.
- In one PostgreSQL transaction, the command inserts the salon with PendingVerification status and the active Owner membership. Migration 010's deferred database constraint validates the active Owner/MFA invariant at commit.
- Any error rolls back both inserts. A second invocation for an identity already attached to a salon is rejected.
- The command never enables MFA, creates an identity, provisions additional members, or changes tenant status.

## Tests and validation

Local:
- dotnet restore Atendee.sln --locked-mode: passed.
- dotnet build Atendee.sln --no-restore --configuration Release -v:q: passed, 0 warnings and 0 errors.
- Domain checks: 19/19 passed.
- CLI --help path executed successfully without requiring a database connection string.
- git diff --check: passed.

CI run 44234622468, exact code SHA 37300e50e2c27c70ac30d820fe83034c71ae4b45, PostgreSQL 18.7:
- Locked dependency restore: passed.
- Release build: passed.
- Domain checks: 19/19.
- EF Core/PostgreSQL RLS persistence checks: passed (10/10).
- Identity/TOTP/tenant-context checks: 39/39.
- Identity log explicitly confirms:
  - pre-MFA identity cannot be provisioned;
  - atendee_auth_login cannot invoke trusted provisioning;
  - successful provisioning creates PendingVerification tenant and attaches the requested MFA-ready identity as Owner;
  - a second provisioning attempt for an identity with existing membership is rejected.
- API HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: passed.
- Disposable PostgreSQL container and CI credentials were cleaned up.

## Operational prerequisites still outside this tool

A trusted process must already have created the operational identity and confirmed its email before the user can sign in and enroll MFA. V41 does not define or implement that identity-creation/password-delivery workflow. Do not silently expand this tool to create identities or issue passwords; obtain a separate owner decision on that workflow first.

## Security and product boundaries

- No public signup or provisioning API.
- No Platform Admin model or endpoint.
- No tenant-visible audit read/export path until the owner decides visibility, retention and post-Closed handling.
- No tenant-status mutation flow and no change to the approved tenant-status access matrix.
- No changes to main; no deployment.

## Next safe step

Ask the owner to decide how the initial operational identity is created and how its password/email confirmation are handled, or have the owner provide an identity that has already completed the trusted process and MFA enrollment. Do not create a public signup flow. Audit visibility, retention and post-Closed behavior remain separate unresolved product decisions.

## Completion state

V41 code commit 37300e50e2c27c70ac30d820fe83034c71ae4b45 was pushed to security/csrf-webhook-antiforgery-v23 and passed PostgreSQL 18.7 CI run 44234622468 for that exact SHA. Local and remote branch SHAs matched at verification. The V41 status artifact and updated continuation index are the required documentation follow-up for this step.
