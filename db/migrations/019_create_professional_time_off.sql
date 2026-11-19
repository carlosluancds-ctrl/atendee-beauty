-- Date-specific professional time off, expressed in tenant-local calendar dates and wall-clock times.
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
            'professional_availability.archived',
            'professional_time_off.created', 'professional_time_off.archived'
        )
    );

CREATE TABLE atendee.professional_time_off
(
    tenant_id UUID NOT NULL,
    time_off_id UUID NOT NULL,
    professional_id UUID NOT NULL,
    local_date DATE NOT NULL,
    start_time TIME WITHOUT TIME ZONE,
    end_time TIME WITHOUT TIME ZONE,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_professional_time_off PRIMARY KEY (tenant_id, time_off_id),
    CONSTRAINT fk_professional_time_off_professional
        FOREIGN KEY (tenant_id, professional_id)
        REFERENCES atendee.professionals (tenant_id, professional_id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_professional_time_off_interval CHECK (
        (start_time IS NULL AND end_time IS NULL) OR
        (start_time IS NOT NULL AND end_time IS NOT NULL AND start_time < end_time)
    ),
    CONSTRAINT ck_professional_time_off_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_professional_time_off_tenant_professional_active_date
    ON atendee.professional_time_off
        (tenant_id, professional_id, is_active, local_date, start_time);
CREATE UNIQUE INDEX ux_professional_time_off_active_full_day
    ON atendee.professional_time_off (tenant_id, professional_id, local_date)
    WHERE is_active AND start_time IS NULL;
CREATE UNIQUE INDEX ux_professional_time_off_active_interval
    ON atendee.professional_time_off
        (tenant_id, professional_id, local_date, start_time, end_time)
    WHERE is_active AND start_time IS NOT NULL;

ALTER TABLE atendee.professional_time_off ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.professional_time_off FORCE ROW LEVEL SECURITY;

CREATE POLICY professional_time_off_runtime_isolation
ON atendee.professional_time_off
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE POLICY professional_time_off_owner_access
ON atendee.professional_time_off
FOR ALL
TO atendee_owner
USING (true)
WITH CHECK (true);

CREATE FUNCTION atendee.validate_professional_time_off()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog, atendee
AS $function$
BEGIN
    IF NOT NEW.is_active THEN
        RETURN NEW;
    END IF;

    -- Acquire the same per-professional lock before any direct SQL write.
    -- The deferred constraint trigger then validates the final transaction state.
    PERFORM pg_advisory_xact_lock(
        hashtextextended(NEW.tenant_id::text || ':' || NEW.professional_id::text, 17319));

    IF NOT EXISTS (
        SELECT 1
        FROM atendee.professionals professional
        WHERE professional.tenant_id = NEW.tenant_id
          AND professional.professional_id = NEW.professional_id
          AND professional.is_active
    ) THEN
        RAISE EXCEPTION 'Time off requires an active professional in the same tenant';
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.validate_professional_time_off() FROM PUBLIC;

CREATE TRIGGER tr_professional_time_off_validate
    BEFORE INSERT OR UPDATE ON atendee.professional_time_off
    FOR EACH ROW
    EXECUTE FUNCTION atendee.validate_professional_time_off();

CREATE FUNCTION atendee.enforce_professional_time_off_no_overlap()
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

    -- Matches the application lock so concurrent writes and direct SQL writes serialize.
    PERFORM pg_advisory_xact_lock(
        hashtextextended(NEW.tenant_id::text || ':' || NEW.professional_id::text, 17319));

    SELECT count(*)::integer
      INTO active_interval_count
      FROM atendee.professional_time_off existing
     WHERE existing.tenant_id = NEW.tenant_id
       AND existing.professional_id = NEW.professional_id
       AND existing.local_date = NEW.local_date
       AND existing.is_active
       AND existing.time_off_id <> NEW.time_off_id;

    IF NEW.start_time IS NULL THEN
        IF active_interval_count > 0 THEN
            RAISE EXCEPTION USING
                ERRCODE = '23P01',
                MESSAGE = 'A full-day time-off entry cannot coexist with another entry on the same date';
        END IF;
        RETURN NEW;
    END IF;

    IF active_interval_count >= 4 THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            MESSAGE = 'A professional date cannot contain more than four active time-off intervals';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM atendee.professional_time_off existing
        WHERE existing.tenant_id = NEW.tenant_id
          AND existing.professional_id = NEW.professional_id
          AND existing.local_date = NEW.local_date
          AND existing.is_active
          AND existing.time_off_id <> NEW.time_off_id
          AND (
              existing.start_time IS NULL OR
              (existing.start_time < NEW.end_time AND NEW.start_time < existing.end_time)
          )
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23P01',
            MESSAGE = 'Professional time-off intervals cannot overlap on the same local date';
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.enforce_professional_time_off_no_overlap() FROM PUBLIC;

CREATE CONSTRAINT TRIGGER tr_professional_time_off_no_overlap
    AFTER INSERT OR UPDATE ON atendee.professional_time_off
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION atendee.enforce_professional_time_off_no_overlap();

CREATE FUNCTION atendee.audit_professional_time_off_change()
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
        RAISE EXCEPTION 'Trusted tenant actor context is required for professional time-off mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'professional_time_off.created';
    ELSIF OLD.is_active AND NOT NEW.is_active THEN
        audit_type := 'professional_time_off.archived';
    ELSE
        audit_type := 'professional_time_off.created';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, related_resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type,
         NEW.professional_id, NEW.time_off_id, clock_timestamp());

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_professional_time_off_change() FROM PUBLIC;

CREATE TRIGGER tr_professional_time_off_audit
    AFTER INSERT OR UPDATE ON atendee.professional_time_off
    FOR EACH ROW
    EXECUTE FUNCTION atendee.audit_professional_time_off_change();

GRANT SELECT, INSERT, UPDATE ON atendee.professional_time_off TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.professional_time_off FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
