# Operational identity activation and email delivery — v1

**Status:** approved technical direction; implementation pending. This document is an architecture decision, not a claim that activation, AWS SES, or production email delivery is implemented.

## 1. Decision and scope

The owner delegated routine technical choices to the assistant and asked to be interrupted only for strictly necessary decisions. The default transactional email provider is therefore selected as **Amazon Simple Email Service (Amazon SES), API v2**, using the AWS SDK for .NET v4 package `AWSSDK.SimpleEmailV2`.

This work implements the previously approved protected operator workflow for the first operational identity. It does not authorize public self-registration, public tenant creation, a public admin endpoint, or a deployment.

V41 remains the sole first-salon/first-Owner provisioning operation. The new identity-provisioning command is a separate executable and must never create a salon or tenant membership.

## 2. Required end-to-end sequence

1. An authorized operator runs a dedicated offline identity-provisioning command from a controlled operations environment.
2. The command validates the dedicated database login and normalizes the supplied email address.
3. It creates (or safely re-issues activation for) a pending ASP.NET Core Identity account with no password and `EmailConfirmed = false`. It must not enable MFA or create salon membership.
4. It generates a cryptographically random, 256-bit, single-use activation token with a 30-minute lifetime. Only SHA-256(token) is persisted in a dedicated activation-token table. The raw token exists only in process memory long enough to compose the email.
5. The command sends a transactional activation email through SES API v2. It reports only whether SES accepted the request and a non-secret correlation/message identifier; it must not print the activation URL or token.
6. The activation link carries the token in the URL fragment (not the query string) so browsers do not send it in HTTP requests, referrer headers, or ordinary server access logs. The frontend will later extract the fragment and POST the token and the user's chosen password to the activation API.
7. The API validates the token and password, atomically consumes the token, sets the password using ASP.NET Core Identity's configured hasher, and sets `EmailConfirmed = true` in the same database transaction. The API does not issue an application cookie or create a salon membership.
8. The user signs in. Existing pre-membership MFA enrollment creates the MFA-ready identity without issuing an application session.
9. Only after email confirmation and MFA enrollment may the existing V41 command create the PendingVerification salon and active Owner membership atomically.
10. The Owner signs in again and completes a fresh MFA challenge before receiving the normal application session.

## 3. Database and Identity constraints

- Use the existing `OperationalIdentityUser`, `UserManager<OperationalIdentityUser>`, `AtendeeIdentityDbContext`, and ASP.NET Core Identity password hasher. Do not implement password hashing.
- Use a dedicated `atendee_identity_provisioner` LOGIN role, `NOINHERIT`, `NOSUPERUSER`, `NOBYPASSRLS`, with SET membership only in `atendee_identity_owner`. Do not reuse `atendee_auth_login`, `atendee_app_login`, postgres, or a superuser for provisioning.
- The provisioning executable verifies `session_user = 'atendee_identity_provisioner'` before it performs any mutation. It is not registered in API dependency injection and has no HTTP route.
- Add a narrowly scoped activation-token table in schema `identity`: token hash, identity user ID, creation time, expiry, and nullable consumed time. Add a foreign key, unique token-hash constraint, useful expiry index, and a partial unique constraint allowing at most one unconsumed activation token per identity.
- Token redemption must use one atomic conditional update (`consumed_at IS NULL AND expires_at > now()`) and return the identity ID only when exactly one row was consumed. Concurrent redemption and replay must fail.
- Token consumption, Identity password creation, email confirmation, and the security audit event commit in one transaction. Any password-policy or persistence failure rolls back token consumption.
- Duplicate normalized email must not create a second account. Re-issuing an activation for a pending identity rotates/replaces the previous token; an already confirmed identity is rejected without exposing account existence to public callers.
- Activation tokens are not ASP.NET Identity email-confirmation tokens because this workflow needs an explicit database-backed single-use/replay guard.
- Keep the activation token out of audit rows, database logs, command-line arguments, telemetry, exception messages, and structured logs. Do not persist raw token or activation URL.

## 4. API contract and abuse controls

- Add only a public `POST /api/v1/auth/activate` endpoint that redeems an existing operator-issued token. It must never create an identity from request-supplied email alone.
- Request body contains the token and new password. Validate request size and password through Identity's configured policy. Do not accept user ID, email-confirmed flag, tenant ID, role, MFA state, or actor ID from the client.
- Preserve the existing global antiforgery rule for unsafe methods; add a dedicated activation rate-limit policy and generic invalid/expired/used-token response.
- Set `Cache-Control: no-store`; never reflect the token or password in responses.
- Activation success does not authenticate the user. A normal login and the existing MFA setup flow are required.
- Do not disclose whether an email address or identity exists through the operator-facing public API surface. Operator command output may identify the specific pending account to the authorized operator, but must not include secrets.

## 5. Email delivery: Amazon SES API v2

- Use `AWSSDK.SimpleEmailV2` and `AmazonSimpleEmailServiceV2Client.SendEmailAsync`; reuse a thread-safe client for the service lifetime.
- Put the sender address, AWS Region, public activation base URL, and optional SES configuration-set name in validated configuration. Never hardcode them or commit credentials.
- Use the AWS SDK credential provider chain. Prefer an IAM role on AWS compute; for an external Windows Server/operations host use temporary credentials/federated access (for example IAM Roles Anywhere) rather than long-lived access keys. No AWS credentials belong in the repository, command line, or logs.
- Attach a least-privilege IAM policy limited to SES send operations and the verified sender identity. Do not grant broad SES administration to the application.
- The sending domain/address must be verified in the selected SES Region. While the SES account remains in the sandbox, recipients must also be verified. Request transactional production access before sending to unverified customer addresses.
- Configure Easy DKIM and publish the exact DNS records SES returns. Configure SPF, DMARC, custom MAIL FROM where appropriate, and account-level bounce/complaint suppression. Never guess or overwrite existing DNS records.
- SES acceptance means the provider queued the message; it does not prove inbox delivery. Log only the SES message ID and non-sensitive correlation ID.
- Use a plain-text and HTML message with a short-lived activation link, no marketing content, no third-party tracking pixels, and no open/click tracking for security activation.
- On transient send failure, retry with bounded exponential backoff. If delivery still fails after the database account/token is created, the operator can safely re-issue activation; the command rotates the token and sends a new link. It must clearly report that identity creation succeeded but delivery failed, without exposing the token.
- Do not add a fake/no-op sender that reports success. Missing or invalid SES configuration must fail closed and be visible as an operational error.

## 6. Tests required before declaring the backend step complete

- Provisioning command rejects every database login except `atendee_identity_provisioner`.
- New identity is pending, email-unconfirmed, has no password, no MFA and no salon membership.
- Duplicate normalized email is rejected; re-issue rotates the token; confirmed identity cannot be re-issued.
- Token is 256-bit random, only its hash is stored, expiry is enforced, and replay/concurrent redemption succeeds at most once.
- Token redemption + password + email confirmation + audit are atomic; password-policy/database failure leaves token usable and account unconfirmed.
- Activation does not issue a session; subsequent login reaches pre-membership MFA setup; V41 refuses Owner provisioning before both email confirmation and MFA.
- Invalid/expired/replayed token responses do not disclose identity existence.
- Rate limiting and antiforgery apply; secrets are absent from logs and exception output.
- SES adapter tests use a fake AWS client and assert sender, recipient, region, message body and cancellation behavior. Integration smoke test remains disabled unless explicitly configured with an SES test identity; CI must never send real email.
- Full PostgreSQL 18.7 CI must restore in locked mode, build with zero warnings, and pass every existing and new check without SKIP.

## 7. External prerequisites and release boundary

The software can be implemented and tested without live AWS credentials by using a fake SES client in automated tests. Live delivery requires an AWS account/Region, a verified sender domain, DNS control for DKIM/SPF/DMARC, SES production access, and temporary AWS credentials with the narrow send policy. These are external infrastructure prerequisites, not reasons to weaken the security design.

No production deployment or real email send is authorized by this architecture decision. The application remains not production-ready until all security, operational, and deployment gates are independently completed.
