# Atendee Beauty — Continuity Status V42 (2026-11-18)

## Scope
Record the owner's decision for the next operational-identity onboarding increment. Documentation/decision checkpoint only; no application code, database migration, API endpoint, email integration, secret, or deployment was changed.

## Owner decision
On 2026-11-18, the owner selected option 1: create the first operational identity through a separate, protected operator command that initiates account activation. This is distinct from V41's first-Owner salon provisioning command.

## Existing V41 boundary retained
V41 already provides an offline first-Owner provisioning command. It accepts only an existing identity whose email is confirmed, MFA is enabled, lockout is inactive, and which has no salon membership. It then creates a PendingVerification salon and active Owner membership atomically. Do not duplicate or broaden that command.

The new identity-creation command must be a separate tool and must not create a salon or membership. The user must complete the activation process, confirm the email, set a password, and enroll MFA before the existing V41 command can attach the first Owner membership. No public self-registration or public salon-provisioning endpoint is approved.

## Security design constraints for the next implementation
- Use ASP.NET Core Identity APIs and its configured password hasher; never implement password hashing directly.
- Do not accept passwords, activation tokens, email-confirmation tokens, or recovery codes as command-line arguments.
- Do not print activation secrets/tokens to console, logs, CI output, or audit records.
- Use high-entropy, single-use, expiring activation material; persist only a cryptographic hash for any application-managed opaque activation token.
- Bind activation to the intended normalized email and the newly created identity; reject replay, expiry, duplicate email, and already activated accounts without account-enumeration leakage.
- The activation flow must set the password and confirm the email before allowing sign-in/MFA enrollment. Existing pre-membership MFA enrollment remains the only path to make the identity eligible for V41.
- Keep the tool outside API dependency injection and expose no generic account-creation HTTP endpoint.
- Record a security audit event without secrets or raw activation material.
- Keep the workflow idempotent or fail safely on retries; never create duplicate identities for the same normalized email.
- Do not select or configure an outbound email provider implicitly.

## External dependency still open
No outbound email provider has been selected in the repository. The owner approved the protected command and activation workflow, but has not selected the transactional email delivery provider. Do not implement real email delivery until that provider and its credential/configuration boundary are approved. A provider-neutral interface and tests may be designed only if they do not leak activation material or create a fake-success delivery path.

## Verified repository state at this checkpoint
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: `security/csrf-webhook-antiforgery-v23`
- Remote HEAD before this documentation checkpoint: `5e37dbbcc23ddd40a7cf478b2b2947ad74bfcba5`
- Most recent code CI before this checkpoint: run 44235109072, completed success for exact code SHA `5e37dbbcc23ddd40a7cf478b2b2947ad74bfcba5`: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44235109072
- `main` remains untouched; no deployment was performed.
- The prior local Windows clone/working tree was not accessible in this session; local cleanliness/build status was not independently verified here.

## Files changed in V42
- `docs/continuity/CONTINUATION_STATUS_V42_2026-11-18.md` — this decision record.
- `docs/continuity/CONTINUE_HERE.md` — updated current decision and next step.
- `docs/continuity/manifest.json` — updated continuity metadata and verified code/CI checkpoint.

## Validation
- Documentation content was written directly to the existing work branch using a single Git commit.
- No code tests or build were run because this checkpoint changes documentation only.
- No new CI run is claimed for the documentation-only commit.
- Confirm the resulting commit, branch HEAD, and all three file paths after the write.

## Next safe step
Ask the owner to select the transactional email provider for activation delivery. Then inspect the provider's official integration and secret-management requirements, design the activation contract, and present the concrete code-change plan before implementation. Preserve the no-public-signup boundary and the V41 pre-membership MFA invariant.
