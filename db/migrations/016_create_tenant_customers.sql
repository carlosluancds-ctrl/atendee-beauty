-- Tenant-scoped customer contacts with RLS, soft archive and mandatory audit.
-- Run as atendee_migrator after migrations 001-015.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_owner;

ALTER TABLE atendee.tenant_audit_events
    DROP CONSTRAINT ck_tenant_audit_event_type;

ALTER TABLE atendee.tenant_audit_events
    ADD CONSTRAINT ck_tenant_audit_event_type CHECK (
        event_type IN (
            'service.created', 'service.updated', 'service.archived',
            'professional.created', 'professional.updated',
            'professional_service.assigned', 'professional_service.unassigned',
            'customer.created', 'customer.updated', 'customer.archived'
        )
    );

CREATE TABLE atendee.customers
(
    tenant_id UUID NOT NULL,
    customer_id UUID NOT NULL,
    full_name VARCHAR(120) NOT NULL,
    normalized_phone VARCHAR(16) NULL,
    email_address VARCHAR(254) NULL,
    is_archived BOOLEAN NOT NULL DEFAULT false,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_customers PRIMARY KEY (tenant_id, customer_id),
    CONSTRAINT ck_customers_name CHECK (
        length(btrim(full_name)) BETWEEN 1 AND 120
        AND full_name !~ '[[:cntrl:]]'
    ),
    CONSTRAINT ck_customers_phone CHECK (
        normalized_phone IS NULL OR normalized_phone ~ '^\+[1-9][0-9]{7,14}$'
    ),
    CONSTRAINT ck_customers_email CHECK (
        email_address IS NULL OR (
            length(email_address) BETWEEN 3 AND 254
            AND email_address = lower(btrim(email_address))
            AND email_address !~ '[[:cntrl:]]'
            AND position('@' IN email_address) > 1
        )
    ),
    CONSTRAINT ck_customers_contact CHECK (
        normalized_phone IS NOT NULL OR email_address IS NOT NULL
    ),
    CONSTRAINT ck_customers_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_customers_tenant_active_name
    ON atendee.customers (tenant_id, is_archived, full_name, customer_id);
CREATE INDEX ix_customers_tenant_phone
    ON atendee.customers (tenant_id, normalized_phone)
    WHERE normalized_phone IS NOT NULL;
CREATE INDEX ix_customers_tenant_email
    ON atendee.customers (tenant_id, email_address)
    WHERE email_address IS NOT NULL;

ALTER TABLE atendee.customers ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.customers FORCE ROW LEVEL SECURITY;

CREATE POLICY customers_runtime_isolation
ON atendee.customers
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE FUNCTION atendee.audit_customer_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    actor_text text;
    audit_type text;
BEGIN
    actor_text := NULLIF(current_setting('app.actor_id', true), '');
    IF actor_text IS NULL THEN
        RAISE EXCEPTION 'Trusted tenant actor context is required for customer mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'customer.created';
    ELSIF NOT OLD.is_archived AND NEW.is_archived THEN
        audit_type := 'customer.archived';
    ELSE
        audit_type := 'customer.updated';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type, NEW.customer_id, clock_timestamp());

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_customer_change() FROM PUBLIC;

CREATE TRIGGER tr_customers_audit
    AFTER INSERT OR UPDATE ON atendee.customers
    FOR EACH ROW
    EXECUTE FUNCTION atendee.audit_customer_change();

GRANT SELECT, INSERT, UPDATE ON atendee.customers TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.customers FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
