# First Owner provisioning - internal operator runbook

Status: implemented as an offline operator command. This is not a public signup or HTTP endpoint.

## Required sequence

1. Create the operational identity through the separately approved trusted identity process. This command does not create identities or set passwords.
2. Confirm the identity email through that process.
3. The identity owner signs in with its existing password and completes the V40 pre-membership MFA enrollment flow. The enrollment response is mfa_enrolled_membership_pending; no application session is issued.
4. An authorized operator obtains the identity GUID from the trusted identity record and runs this command from a controlled operations host.
5. The command verifies that the database session is atendee_migrator, the identity exists, email is confirmed, MFA is enabled, the identity is not locked out, and it has no salon membership.
6. In one PostgreSQL transaction, the command creates the salon in PendingVerification and attaches the existing identity as its active Owner. Migration 010's deferred constraint validates the active Owner/MFA invariant at commit.
7. The Owner signs in again and completes a fresh MFA challenge before receiving an application session.

## Credentials and execution boundary

- Use only the dedicated atendee_migrator login, which is NOINHERIT, non-superuser and non-BYPASSRLS. The tool switches transaction-locally to the two existing NOLOGIN owner roles for the minimum operations it needs.
- Inject ATENDEE_PROVISIONING_CONNECTION_STRING from the approved secret manager. Never commit it, put it in source control, pass it as a command-line argument, or print it.
- Run from a restricted operations workstation or runner. Do not expose this command through the API, a web page, or a general-purpose job endpoint.
- Do not use a superuser, postgres, atendee_auth_login, or atendee_app_login connection. The tool rejects sessions whose session_user is not exactly atendee_migrator.
- Treat command output containing tenant and identity GUIDs as operational data. Keep the audit and change record in the approved operations system.

## Command

    dotnet run --project tools/Atendee.FirstOwnerProvisioning -- --identity-user-id "00000000-0000-0000-0000-000000000000" --tenant-name "Salon name" --tenant-slug "unique-salon-slug" --time-zone "America/Sao_Paulo" --owner-display-name "Owner display name"

The GUID above is a placeholder. Replace it with the GUID of the already-created, MFA-enabled identity. The slug must be globally unique and the time zone must be a valid IANA or Windows time-zone ID.

## Failure behavior

- Missing identity, unconfirmed email, disabled MFA, active lockout, or existing membership: reject before inserting a tenant.
- Wrong database login: reject before changing database roles or data.
- Duplicate slug, database error, or constraint failure: transaction rolls back both tenant and membership.
- Re-running for an identity that already has membership: reject; it does not create another salon.
- The command never enables MFA, generates recovery codes, creates credentials, changes tenant status, or provisions additional members.

## Validation

The Identity/PostgreSQL integration checks exercise the MFA precondition, restricted database login, atomic tenant + Owner creation, and rejection of a second provisioning attempt. CI runs these checks against disposable PostgreSQL 18.7 after migrations 001-010.
