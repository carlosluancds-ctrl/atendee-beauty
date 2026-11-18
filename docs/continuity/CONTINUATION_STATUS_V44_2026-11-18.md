# Atendee Beauty — Continuity Status V44 (2026-11-18)

## Scope
First backend implementation of the approved offline operational-identity creation and email-activation flow. Backend-only increment. The existing V41 first-Owner provisioning command was not modified. No frontend, AWS resource, DNS record, credential, live email, production data, or deployment was changed.

## Owner direction and boundaries
The owner delegated routine technical decisions and asked to be interrupted only for strictly necessary external access or decisions. This increment follows the approved V42/V43 design:
- No public self-registration, public account creation, public tenant creation, or public first-Owner endpoint.
- A separate offline operator command creates or re-issues a pending operational identity.
- The activation API accepts only a token previously issued by the operator command.
- Activation sets a password and confirms email atomically, but does not issue a session, enroll MFA, create a salon, or create membership.
- The user signs in and completes the existing pre-membership MFA flow; only then can the unchanged V41 command create the first Owner.
- Amazon SES API v2 remains the chosen transactional email provider.

## Implemented in V44
- Added the dedicated atendee_identity_provisioner login to the fresh-install role template and an existing-database DBA bootstrap script. The login is NOINHERIT, NOSUPERUSER and NOBYPASSRLS, with SET-only membership in atendee_identity_owner. The command verifies session_user before mutation.
- Added migration 011 for one activation-token row per identity, SHA-256 hash only, 30-minute expiry, single-use consumption, token rotation, and audit event allowlisting. The API role can read tokens and update only ConsumedAtUtc; it cannot issue or rotate tokens.
- Added the offline Atendee.OperationalIdentityProvisioning console tool with create and reissue modes. It prompts for email; it never accepts or prints the raw token as a command-line argument.
- Added SES v2 email submission using AWS SDK for .NET v4 AWSSDK.SimpleEmailV2 4.1.0.2 and the default AWS credential provider chain. The tool reports SES acceptance, not delivery.
- Added POST /api/v1/auth/activate, antiforgery enforcement inherited from the global unsafe-request middleware, IP rate limiting, generic invalid/expired/replayed responses, and no-store handling.
- Activation uses ASP.NET Core Identity password validation and hashing. The token row and user row are locked in a consistent order; password creation, email confirmation, token consumption, and audit insertion share one database transaction. No application cookie is issued.
- Added PostgreSQL identity integration checks for pending creation, normalized duplicate rejection, hash-only persistence, token rotation, password-policy failure, concurrent single-use redemption, replay rejection, and confirmed-account re-issue rejection.
- Added HTTP integration checks for activation, no session, no membership, password-policy failure, and replay rejection.
- Added the operational runbook at docs/operations/OPERATIONAL_IDENTITY_PROVISIONING.md.
- Updated the PostgreSQL 18.7 CI workflow to provision the dedicated role, apply migration 011, and provide a separate provisioner connection string to the integration checks.

## Validation actually observed
- dotnet restore Atendee.sln --locked-mode: passed.
- dotnet build Atendee.sln --no-restore --configuration Release: passed with 0 warnings and 0 errors.
- Atendee.Domain.Checks: 19/19 passed.
- PostgreSQL-dependent Identity, Persistence and API HTTP integration checks were not run in the temporary Windows environment because it has no PostgreSQL server or Docker. They are wired into the repository's PostgreSQL 18.7 CI workflow; no local pass is claimed for them.
- Full CI result for this commit is pending after push.

## External prerequisites not performed
- No SES API call was made and no real email was sent.
- The SES sender identity/domain, Easy DKIM, SPF/DMARC, production access, IAM policy and temporary AWS credentials are not configured by this increment.
- No database migration was applied to a production or shared database.
- No deployment was performed.

## Work environment and repository
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23; main remains untouched.
- Base commit: 86a6a2934063b19998e3d94edc119106a69b411e (V43).
- The code was staged and compiled in a temporary remote snapshot at C:\Temp\atendee-work, populated from the exact branch tree through the GitHub connection. This is not a persistent clone of the user's local workstation.
- No production credentials or real customer data were used.

## Next step
Push this backend increment to the work branch, verify the exact commit and CI run on PostgreSQL 18.7, fix any integration failures, and update continuity status with the observed CI result. Only after the backend CI gate passes should frontend activation UI work begin. Live SES setup remains an external-access boundary and is not authorized by this code increment.