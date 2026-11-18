# Operational identity provisioning and activation

This is an offline operator workflow. It is not a public registration endpoint and it does not create a salon, a membership, or an authenticated session.

## Database prerequisites

1. Apply migrations 001-010 as documented for the existing installation.
2. On a new installation, db/security/001_roles_template.sql creates the dedicated atendee_identity_provisioner login and grants it SET membership in atendee_identity_owner.
3. On an existing installation, run db/security/002_identity_provisioner_role.sql once as the database DBA. It creates the restricted login if missing and grants database connection plus SET membership. It does not set a password.
4. Provision the login's authentication credential through the approved database secret-management process. Do not add a password to source control.
5. Apply db/migrations/011_operational_identity_activation.sql as atendee_migrator.

The provisioner login is NOINHERIT, NOSUPERUSER, and NOBYPASSRLS. The command verifies session_user and then uses SET LOCAL ROLE atendee_identity_owner inside one transaction. Do not run it with atendee_migrator, atendee_auth_login, or an application login.

## Runtime configuration

Set these environment variables in the operator's protected execution environment:

- ConnectionStrings__IdentityProvisioner: connection string for the dedicated atendee_identity_provisioner login.
- AWS_REGION: AWS region containing the SES v2 verified sender identity.
- ATENDEE_SES_FROM_EMAIL: verified sender email address.
- ATENDEE_ACTIVATION_BASE_URL: HTTPS frontend activation page URL, without query or fragment.
- ATENDEE_SES_CONFIGURATION_SET: optional SES configuration set.

The AWS SDK for .NET v4 default credential provider chain is used. Prefer an IAM role on AWS compute or temporary/federated credentials on an external Windows Server. Grant only ses:SendEmail for the approved verified sender identity. Never put permanent AWS access keys in the repository, command arguments, or logs.

Before sending real email, verify the sender domain or address in SES, configure Easy DKIM and the domain's SPF/DMARC records, and request SES production access if the account is still in the sandbox. In the sandbox, recipient addresses must also be verified. DNS changes require control of the sender domain and must be reviewed with its DNS administrator.

## Commands

From the repository root:

~~~powershell
dotnet run --project tools/Atendee.OperationalIdentityProvisioning/Atendee.OperationalIdentityProvisioning.csproj -- create
dotnet run --project tools/Atendee.OperationalIdentityProvisioning/Atendee.OperationalIdentityProvisioning.csproj -- reissue
~~~

The command prompts for the email address. create creates a new pending identity. reissue is allowed only for an existing, unlocked, unactivated identity with no password and no MFA. It replaces the prior token; any previous activation link becomes invalid.

The command generates a cryptographically random 256-bit token with a 30-minute lifetime. Only its SHA-256 hash is stored. The raw token stays in process memory long enough to compose the SES email; it is not printed, persisted, passed as a command-line argument, or written to audit records. If SES submission fails or is ambiguous, run reissue; the original raw token cannot be recovered.

A successful SES response means SES accepted the message submission. It is not proof of inbox delivery.

## Activation and onboarding sequence

1. The frontend reads the token from the URL fragment (#token=...). The fragment is not sent in the HTTP request or referrer.
2. The frontend posts the token and chosen password to POST /api/v1/auth/activate, with the normal antiforgery cookie and X-CSRF-TOKEN header.
3. The API applies rate limiting, locks and consumes the token atomically, sets the password through ASP.NET Core Identity, confirms the email, and writes a security audit event.
4. Activation returns activated_sign_in_required. It never issues an authentication cookie, creates a salon, or creates membership.
5. The user signs in and enrolls MFA through the existing pre-membership flow.
6. Only after email confirmation and MFA enrollment may the existing Atendee.FirstOwnerProvisioning command create the first salon and attach its Owner. That V41 command remains unchanged.

Invalid, expired, and replayed tokens share a generic response. Password-policy failures do not consume the activation token. No frontend registration route or public identity-creation API is introduced.