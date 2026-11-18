-- Operator-issued, single-use email activation tokens for pending operational identities.
-- The dedicated atendee_identity_provisioner login must be provisioned first
-- (see db/security/002_identity_provisioner_role.sql on existing databases).
-- Run as atendee_migrator after migrations 001-010.
\set ON_ERROR_STOP on
BEGIN;

SET LOCAL ROLE atendee_identity_owner;

CREATE TABLE identity."OperationalIdentityActivationTokens"
(
    "UserId" UUID NOT NULL,
    "TokenHash" BYTEA NOT NULL,
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
    "ExpiresAtUtc" TIMESTAMPTZ NOT NULL,
    "ConsumedAtUtc" TIMESTAMPTZ NULL,
    CONSTRAINT "PK_OperationalIdentityActivationTokens" PRIMARY KEY ("UserId"),
    CONSTRAINT "FK_OperationalIdentityActivationTokens_User"
        FOREIGN KEY ("UserId") REFERENCES identity."AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "CK_OperationalIdentityActivationTokens_HashLength"
        CHECK (octet_length("TokenHash") = 32),
    CONSTRAINT "CK_OperationalIdentityActivationTokens_Expiry"
        CHECK ("ExpiresAtUtc" > "CreatedAtUtc"),
    CONSTRAINT "CK_OperationalIdentityActivationTokens_Consumed"
        CHECK ("ConsumedAtUtc" IS NULL OR "ConsumedAtUtc" >= "CreatedAtUtc")
);

CREATE UNIQUE INDEX "UX_OperationalIdentityActivationTokens_TokenHash"
    ON identity."OperationalIdentityActivationTokens" ("TokenHash");

-- The API can look up and consume an issued token, but cannot issue, rotate or
-- delete tokens. Only the offline provisioner may mutate pending activations.
REVOKE ALL ON TABLE identity."OperationalIdentityActivationTokens"
    FROM PUBLIC, atendee_auth_login;
GRANT SELECT ON TABLE identity."OperationalIdentityActivationTokens"
    TO atendee_auth_login;
GRANT UPDATE ("ConsumedAtUtc") ON TABLE identity."OperationalIdentityActivationTokens"
    TO atendee_auth_login;

ALTER TABLE identity."SecurityAuditEvents"
    DROP CONSTRAINT "CK_SecurityAuditEvents_EventType";

ALTER TABLE identity."SecurityAuditEvents"
    ADD CONSTRAINT "CK_SecurityAuditEvents_EventType" CHECK
        ("EventType" IN (
            'operational_account.disabled',
            'operational_account.sessions_revoked',
            'operational_mfa.enrolled',
            'operational_mfa.totp_challenge_succeeded',
            'operational_mfa.recovery_code_redeemed',
            'operational_mfa.challenge_failed',
            'operational_mfa.enrollment_failed',
            'operational_auth.login_succeeded',
            'operational_auth.login_failed',
            'operational_auth.password_verified_mfa_pending',
            'tenant_membership.created',
            'tenant_membership.role_changed',
            'tenant_membership.activated',
            'tenant_membership.deactivated',
            'tenant_status.changed',
            'operational_identity.activation_issued',
            'operational_identity.activation_reissued',
            'operational_identity.activation_succeeded'
        ));

COMMIT;