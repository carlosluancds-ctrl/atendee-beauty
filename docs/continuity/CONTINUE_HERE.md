# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** read `CLAUDE.md`, this file, and the highest-numbered `docs/continuity/CONTINUATION_STATUS_V*.md` before changing code.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- V42 commit: `84faf49e76ed695b7c4ebaa99c555271bc226ef3`.
- V42 PostgreSQL 18.7 CI: run 44273325577 passed for exact V42 SHA: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44273325577.
- V43 adds only architecture/continuity documentation. Verify the remote HEAD before starting.

## Completed
- V41: offline first-Owner salon provisioning command. Requires an existing identity with confirmed email, MFA enabled, no active lockout and no salon membership; atomically creates PendingVerification salon + active Owner membership. No HTTP endpoint.
- V42: owner approved a separate protected operator command to create the initial operational identity and initiate activation.
- V43: selected Amazon SES API v2 as the default transactional email provider and documented the complete activation design. No code or AWS resource changed yet.
- Detailed design: `docs/technical/OPERATIONAL_IDENTITY_ACTIVATION_DESIGN_V1.md`.

## Approved boundaries and design
- No public self-registration, no public tenant creation, no public account-creation endpoint.
- Separate offline identity-provisioning command; separate from V41.
- Pending ASP.NET Identity account has no password and EmailConfirmed=false.
- 256-bit random activation token, single-use, 30-minute expiry; persist only SHA-256 hash. Never expose token in CLI, logs, telemetry, exceptions or audit events.
- Activation POST accepts only operator-issued token + new password; atomic token consumption, Identity password creation and email confirmation. No application session is issued.
- Then user signs in and completes existing pre-membership MFA; only then V41 creates the first Owner membership.
- SES v2 through AWS SDK .NET v4 (`AWSSDK.SimpleEmailV2`); credential provider chain; IAM least privilege; no long-lived credentials in repo.
- Dedicated DB login `atendee_identity_provisioner` with SET only to `atendee_identity_owner`.
- SES live delivery needs verified sender domain, DKIM/SPF/DMARC DNS, production access and temporary AWS credentials. No live email or deployment without explicit external authorization.
- Do not add a fake sender that claims success.

## Next backend increments
1. Recheck branch HEAD and CI; get a usable clean local clone/build environment before editing code.
2. Implement dedicated provisioning DB role + activation token migration and PostgreSQL integration checks.
3. Implement offline identity provisioning/re-issue command using ASP.NET Identity.
4. Implement SES v2 sender with fake-client tests and locked package restore.
5. Implement activation API with atomic token/password/email confirmation and abuse controls.
6. Run full PostgreSQL 18.7 CI, review logs, update operational docs.
7. Only after backend gates pass, begin frontend activation UI.

## Continuity rule
After every completed increment, create a new versioned status file, update this index and manifest, commit directly to this branch, then verify exact SHA and files. Preserve all prior versions. Record only tests/CI actually observed. No main changes, no deployment, no production data.
