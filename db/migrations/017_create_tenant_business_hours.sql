-- Tenant-scoped weekly opening hours stored as local wall-clock intervals.
-- Empty schedule means closed; all times are interpreted using tenants.time_zone_id.
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
            'business_hours.created', 'business_hours.updated', 'business_hours.archived'
        )
    );

CREATE TABLE atendee.business_hours
(
    tenant_id UUID NOT NULL,
    business_hours_id UUID NOT NULL,
    iso_day_of_week SMALLINT NOT NULL,
    start_time TIME WITHOUT TIME ZONE NOT NULL,
    end_time TIME WITHOUT TIME ZONE NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_business_hours PRIMARY KEY (tenant_id, business_hours_id),
    CONSTRAINT ck_business_hours_day CHECK (iso_day_of_week BETWEEN 1 AND 7),
    CONSTRAINT ck_business_hours_interval CHECK (start_time < end_time),
    CONSTRAINT ck_business_hours_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_business_hours_tenant_active_day
    ON atendee.business_hours (tenant_id, is_active, iso_day_of_week, start_time);
CREATE UNIQUE INDEX ux_business_hours_active_interval
    ON atendee.business_hours (tenant_id, iso_day_of_week, start_time, end_time)
    WHERE is_active;

ALTER TABLE atendee.business_hours ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.business_hours FORCE ROW LEVEL SECURITY;

CREATE POLICY business_hours_runtime_isolation
ON atendee.business_hours
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE POLICY business_hours_owner_access
ON atendee.business_hours
FOR ALL
TO atendee_owner
USING (true)
WITH CHECK (true);

CREATE FUNCTION atendee.enforce_business_hours_no_overlap()
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

    -- Serialize schedule changes per tenant so concurrent requests cannot
    -- insert overlapping local-time intervals.
    PERFORM pg_advisory_xact_lock(hashtextextended(NEW.tenant_id::text, 17317));

    SELECT count(*)::integer
      INTO active_interval_count
      FROM atendee.business_hours existing
     WHERE existing.tenant_id = NEW.tenant_id
       AND existing.iso_day_of_week = NEW.iso_day_of_week
       AND existing.is_active
       AND existing.business_hours_id <> NEW.business_hours_id;

    IF active_interval_count >= 4 THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            MESSAGE = 'A business day cannot contain more than four active intervals';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM atendee.business_hours existing
        WHERE existing.tenant_id = NEW.tenant_id
          AND existing.iso_day_of_week = NEW.iso_day_of_week
          AND existing.is_active
          AND existing.business_hours_id <> NEW.business_hours_id
          AND existing.start_time < NEW.end_time
          AND NEW.start_time < existing.end_time
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23P01',
            MESSAGE = 'Business-hours intervals cannot overlap for the same tenant and day';
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.enforce_business_hours_no_overlap() FROM PUBLIC;

CREATE CONSTRAINT TRIGGER tr_business_hours_no_overlap
    AFTER INSERT OR UPDATE ON atendee.business_hours
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION atendee.enforce_business_hours_no_overlap();

CREATE FUNCTION atendee.audit_business_hours_change()
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
        RAISE EXCEPTION 'Trusted tenant actor context is required for business-hours mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'business_hours.created';
    ELSIF OLD.is_active AND NOT NEW.is_active THEN
        audit_type := 'business_hours.archived';
    ELSE
        audit_type := 'business_hours.updated';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type,
         NEW.business_hours_id, clock_timestamp());

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_business_hours_change() FROM PUBLIC;

CREATE TRIGGER tr_business_hours_audit
    AFTER INSERT OR UPDATE ON atendee.business_hours
    FOR EACH ROW
    EXECUTE FUNCTION atendee.audit_business_hours_change();

GRANT SELECT, INSERT, UPDATE ON atendee.business_hours TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.business_hours FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
