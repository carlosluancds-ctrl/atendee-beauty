# Atendee Beauty — Continuity Status V43 (2026-11-18)

## Scope
Record the autonomous technical decision for initial identity activation and transactional email delivery. Documentation/architecture checkpoint only; no application code, database migration, AWS resource, DNS record, credential, real email, or deployment was changed.

## Owner direction
The owner stated: "Vá fazendo tudo da forma que achar mais profissional, só pare pra me perguntar o que for estritamente necessário." Routine technical choices may be made autonomously; ask only for external access/ownership decisions that cannot be safely inferred.

## Technical decisions recorded
- Selected Amazon Simple Email Service (Amazon SES), API v2, as the default transactional email provider.
- Planned SDK: AWS SDK for .NET v4, `AWSSDK.SimpleEmailV2`.
- Planned credential strategy: AWS SDK credential provider chain; prefer an IAM role on AWS compute and temporary/federated credentials for external hosts. No long-lived credentials in code or repository.
- Planned onboarding: separate offline identity-provisioning command; pending ASP.NET Identity account without password; 256-bit random one-time activation token; SHA-256 hash stored in PostgreSQL; 30-minute expiry; no token in logs/CLI output; token activation sets password with ASP.NET Identity and confirms email atomically; then pre-membership MFA; then existing V41 first-Owner command.
- Planned activation endpoint redeems only an operator-issued token. It does not create identities from public email requests and does not issue an authenticated session.
- Planned dedicated database login: `atendee_identity_provisioner`, NOINHERIT/NOSUPERUSER/NOBYPASSRLS, SET only to `atendee_identity_owner`.
- Full requirements and test matrix: `docs/technical/OPERATIONAL_IDENTITY_ACTIVATION_DESIGN_V1.md`.

## Existing implementation boundaries retained
- V41 already implements the offline first-Owner salon provisioning command. Do not duplicate it.
- V42 recorded approval for a separate protected identity-creation command. No public self-registration, no public salon provisioning endpoint.
- V41 still requires confirmed email, MFA enabled, no active lockout, and no existing salon membership before atomically creating the PendingVerification salon and active Owner membership.
- Existing pre-membership MFA enrollment is the intended path after activation and before V41.
- The old `wip/operational-identity-v1` branch is an ancestor of the current development line (comparison returned 0 commits ahead and 49 behind); it contains no unique commits to cherry-pick.

## Verified repository and CI state
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: `security/csrf-webhook-antiforgery-v23`
- V42 documentation commit: `84faf49e76ed695b7c4ebaa99c555271bc226ef3`
- V42 workflow run 44273325577 completed success for exact SHA `84faf49e76ed695b7c4ebaa99c555271bc226ef3`: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44273325577
- Previous code baseline with successful PostgreSQL 18.7 CI: `5e37dbbcc23ddd40a7cf478b2b2947ad74bfcba5`, run 44235109072.
- `main` remains untouched; no deployment or live email was performed.
- Remote Desktop Commander device was online, but no local `Atendee.sln` clone was found on its C: or D: drives. No local worktree/build status is claimed.

## Files changed in V43
- `docs/technical/OPERATIONAL_IDENTITY_ACTIVATION_DESIGN_V1.md` — detailed design and security/test requirements.
- `docs/continuity/CONTINUATION_STATUS_V43_2026-11-18.md` — this checkpoint.
- `docs/continuity/CONTINUE_HERE.md` — current decisions and next step.
- `docs/continuity/manifest.json` — version and CI metadata.

## Validation
- V42 CI passed on the exact V42 commit.
- V43 is documentation-only; code build/tests were not run and no new CI result is claimed for V43.
- The resulting V43 commit and all file paths must be re-read from GitHub to verify the write.

## Next step
Implement the design in small backend-only increments: dedicated DB role and activation-token migration; identity provisioning command; SES v2 sender and tests; activation endpoint with atomic token/password/email-confirmation transaction; runbook and end-to-end integration checks. Update the continuity files and commit after each completed increment. Live SES setup requires verified sender domain/DNS and AWS temporary credentials; request only the minimum access or operator action when that boundary is reached.
