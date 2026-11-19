-- Recurring weekly availability for active tenant professionals.
-- Times are local wall-clock values in the tenant's configured time zone.
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
            'customer.created', 'customer.updated', 'customer.archived',
            'business_hours.created', 'business_hours.updated', 'business_hours.archived',
            'professional_availability.created', 'professional_availability.updated',
            'professional_availability.archived'
        )
    );

CREATE TABLE atendee.professional_availability
(
    tenant_id UUID NOT NULL,
    availability_id UUID NOT NULL,
    professional_id UUID NOT NULL,
    iso_day_of_week SMALLINT NOT NULL,
    start_time TIME WITHOUT TIME ZONE NOT NULL,
    end_time TIME WITHOUT TIME ZONE NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_professional_availability PRIMARY KEY (tenant_id, availability_id),
    CONSTRAINT fk_professional_availability_professional
        FOREIGN KEY (tenant_id, professional_id)
        REFERENCES atendee.professionals (tenant_id, professional_id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_professional_availability_day CHECK (iso_day_of_week BETWEEN 1 AND 7),
    CONSTRAINT ck_professional_availability_interval CHECK (start_time < end_time),
    CONSTRAINT ck_professional_availability_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_professional_availability_tenant_professional_active_day
    ON atendee.professional_availability
        (tenant_id, professional_id, is_active, iso_day_of_week, start_time);
CREATE UNIQUE INDEX ux_professional_availability_active_interval
    ON atendee.professional_availability
        (tenant_id, professional_id, iso_day_of_week, start_time, end_time)
    WHERE is_active;

ALTER TABLE atendee.professional_availability ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.professional_availability FORCE ROW LEVEL SECURITY;

CREATE POLICY professional_availability_runtime_isolation
ON atendee.professional_availability
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE POLICY professional_availability_owner_access
ON atendee.professional_availability
FOR ALL
TO atendee_owner
USING (true)
WITH CHECK (true);

CREATE FUNCTION atendee.validate_professional_availability()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog, atendee
AS $function$
BEGIN
    IF NOT NEW.is_active THEN
        RETURN NEW;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM atendee.professionals professional
        WHERE professional.tenant_id = NEW.tenant_id
          AND professional.professional_id = NEW.professional_id
          AND professional.is_active
    ) THEN
        RAISE EXCEPTION 'Availability requires an active professional in the same tenant';
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.validate_professional_availability() FROM PUBLIC;

CREATE TRIGGER tr_professional_availability_validate
    BEFORE INSERT OR UPDATE ON atendee.professional_availability
    FOR EACH ROW
    EXECUTE FUNCTION atendee.validate_professional_availability();

CREATE FUNCTION atendee.enforce_professional_availability_no_overlap()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    active_interval_count integer;
BEGIN
    IF NOT NEW.is_active THEN
        RETURN NEW;
    END IF;

    -- The same tenant/professional lock key is acquired by the application
    -- before a full replacement and here for direct/concurrent database writes.
    PERFORM pg_advisory_xact_lock(
        hashtextextended(NEW.tenant_id::text || ':' || NEW.professional_id::text, 17318));

    SELECT count(*)::integer
      INTO active_interval_count
      FROM atendee.professional_availability existing
     WHERE existing.tenant_id = NEW.tenant_id
       AND existing.professional_id = NEW.professional_id
       AND existing.iso_day_of_week = NEW.iso_day_of_week
       AND existing.is_active
       AND existing.availability_id <> NEW.availability_id;

    IF active_interval_count >= 4 THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            MESSAGE = 'A professional day cannot contain more than four active availability intervals';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM atendee.professional_availability existing
        WHERE existing.tenant_id = NEW.tenant_id
          AND existing.professional_id = NEW.professional_id
          AND existing.iso_day_of_week = NEW.iso_day_of_week
          AND existing.is_active
          AND existing.availability_id <> NEW.availability_id
          AND existing.start_time < NEW.end_time
          AND NEW.start_time < existing.end_time
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23P01',
            MESSAGE = 'Professional availability intervals cannot overlap for the same tenant, professional and day';
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.enforce_professional_availability_no_overlap() FROM PUBLIC;

CREATE CONSTRAINT TRIGGER tr_professional_availability_no_overlap
    AFTER INSERT OR UPDATE ON atendee.professional_availability
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION atendee.enforce_professional_availability_no_overlap();

CREATE FUNCTION atendee.audit_professional_availability_change()
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
        RAISE EXCEPTION 'Trusted tenant actor context is required for professional availability mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'professional_availability.created';
    ELSIF OLD.is_active AND NOT NEW.is_active THEN
        audit_type := 'professional_availability.archived';
    ELSE
        audit_type := 'professional_availability.updated';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, related_resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type,
         NEW.professional_id, NEW.availability_id, clock_timestamp());

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_professional_availability_change() FROM PUBLIC;

CREATE TRIGGER tr_professional_availability_audit
    AFTER INSERT OR UPDATE ON atendee.professional_availability
    FOR EACH ROW
    EXECUTE FUNCTION atendee.audit_professional_availability_change();

GRANT SELECT, INSERT, UPDATE ON atendee.professional_availability TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.professional_availability FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
