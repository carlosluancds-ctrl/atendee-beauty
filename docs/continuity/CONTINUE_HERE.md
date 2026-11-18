# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md before changing code.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23; never modify main.
- V43 base commit: 86a6a2934063b19998e3d94edc119106a69b411e.
- V44 backend implementation is staged locally in a temporary remote snapshot; push/commit verification and PostgreSQL 18.7 CI are the immediate next gate.
- Previous confirmed PostgreSQL 18.7 CI: run 44273325577 passed for exact V42 SHA 84faf49e76ed695b7c4ebaa99c555271bc226ef3.

## Completed
- V41: offline first-Owner salon provisioning command. Requires an existing identity with confirmed email, MFA enabled, no active lockout and no salon membership; atomically creates PendingVerification salon + active Owner membership. No HTTP endpoint.
- V42: owner approved a separate protected operator command to create the initial operational identity and initiate activation.
- V43: selected Amazon SES API v2 as the default transactional email provider and documented the activation design.
- V44: implemented the backend activation path: dedicated provisioner role, migration 011, offline create/reissue CLI, SES v2 email sender, activation API, audit events, integration tests and operator runbook. V41 remains unchanged.

## V44 implementation details
- No public self-registration, public tenant creation, public account creation route, or public first-Owner endpoint.
- Dedicated offline command: tools/Atendee.OperationalIdentityProvisioning; create and reissue modes.
- Pending ASP.NET Identity user has no password, EmailConfirmed=false, TwoFactorEnabled=false, and no salon membership.
- 256-bit random activation token, 30-minute expiry, single-use; only SHA-256 hash stored. Re-issue rotates the previous token.
- API: POST /api/v1/auth/activate. Requires antiforgery; IP rate-limited; generic invalid/expired/replayed response; password creation, email confirmation, token consumption and audit are atomic. No session issued.
- Activation link carries the token in the URL fragment. Frontend must extract it and POST token + new password; frontend activation UI is not implemented yet.
- SES v2 uses AWS SDK for .NET v4 AWSSDK.SimpleEmailV2 4.1.0.2 and the AWS credential provider chain. No permanent credentials in source.
- Database login atendee_identity_provisioner is NOINHERIT/NOSUPERUSER/NOBYPASSRLS and can SET only to atendee_identity_owner. Migration 011 grants the API role SELECT and only column-level UPDATE on ConsumedAtUtc for activation tokens.
- Existing V41 first-Owner command remains the sole way to create the first salon and Owner membership, after email confirmation and MFA.

## Validation observed locally
- dotnet restore Atendee.sln --locked-mode passed.
- dotnet build Atendee.sln --no-restore --configuration Release passed with 0 warnings and 0 errors.
- Atendee.Domain.Checks passed 19/19.
- PostgreSQL-dependent Identity, Persistence and API HTTP integration checks were not run in the temporary Windows environment because PostgreSQL and Docker are unavailable there. They are wired into the PostgreSQL 18.7 CI workflow; do not claim them as passed until CI confirms.
- SES was not called; no real email was sent.

## Immediate next steps
1. Push the V44 code and continuity files to this branch; verify exact commit and file contents.
2. Inspect the triggered PostgreSQL 18.7 CI run, including migration 011, role bootstrap, identity tests, and HTTP activation tests. Fix any failures before proceeding.
3. Update continuity status and manifest with the exact V44 SHA and observed CI result.
4. Only after the backend CI gate passes, implement the frontend activation UI.
5. Configure SES sender identity/DNS, production access, IAM and temporary credentials only when the external AWS/domain access is available and authorized. Never send a real email or deploy without that prerequisite.

## External setup still pending
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials.
- Apply migration 011 and provision the restricted DB login in the target environment.
- Frontend activation page and fragment-to-API flow.
- Platform Admin store/MFA/break-glass, WebAuthn/passkey, production Data Protection protector, distributed rate limiting and trusted proxy configuration.
- No production deployment authorized or performed.

## Continuity rule
After every completed increment, create a new versioned status file, update this index and manifest, commit directly to this branch, then verify exact SHA and files. Preserve all prior versions. Record only tests/CI actually observed. No main changes, no deployment, no production data.