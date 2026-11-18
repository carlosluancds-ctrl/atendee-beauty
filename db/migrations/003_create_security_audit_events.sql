-- Minimal append-only audit ledger for operational identity security actions.
-- Run as atendee_migrator after migrations 001 and 002.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_identity_owner;

CREATE TABLE identity."SecurityAuditEvents"
(
    "Id" UUID NOT NULL,
    "EventType" VARCHAR(80) NOT NULL,
    "ActorIdentityUserId" UUID NULL,
    "TargetIdentityUserId" UUID NOT NULL,
    "OccurredAtUtc" TIMESTAMPTZ NOT NULL,
    "CorrelationId" VARCHAR(64) NULL,
    CONSTRAINT "PK_SecurityAuditEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_SecurityAuditEvents_EventType" CHECK
        ("EventType" IN (
            'operational_account.disabled',
            'operational_account.sessions_revoked',
            'operational_mfa.enrolled',
            'operational_mfa.totp_challenge_succeeded',
            'operational_mfa.recovery_code_redeemed'
        )),
    CONSTRAINT "CK_SecurityAuditEvents_Target" CHECK
        ("TargetIdentityUserId" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_SecurityAuditEvents_Actor" FOREIGN KEY ("ActorIdentityUserId")
        REFERENCES identity."AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SecurityAuditEvents_Target" FOREIGN KEY ("TargetIdentityUserId")
        REFERENCES identity."AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_SecurityAuditEvents_Target_OccurredAtUtc"
    ON identity."SecurityAuditEvents" ("TargetIdentityUserId", "OccurredAtUtc" DESC);

CREATE FUNCTION identity.reject_security_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog, identity
AS $function$
BEGIN
    RAISE EXCEPTION 'Security audit events are append-only.';
END;
$function$;

REVOKE ALL ON FUNCTION identity.reject_security_audit_mutation() FROM PUBLIC;
CREATE TRIGGER "TR_SecurityAuditEvents_AppendOnly"
    BEFORE UPDATE OR DELETE ON identity."SecurityAuditEvents"
    FOR EACH ROW EXECUTE FUNCTION identity.reject_security_audit_mutation();

CREATE TRIGGER "TR_SecurityAuditEvents_NoTruncate"
    BEFORE TRUNCATE ON identity."SecurityAuditEvents"
    FOR EACH STATEMENT EXECUTE FUNCTION identity.reject_security_audit_mutation();

REVOKE ALL ON TABLE identity."SecurityAuditEvents" FROM PUBLIC, atendee_auth_login;
GRANT INSERT ON TABLE identity."SecurityAuditEvents" TO atendee_auth_login;

COMMIT;
