-- Tenant-scoped appointment persistence, immutable event history and transactional outbox.
-- Run as atendee_migrator after migrations 001-019.
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
            'professional_time_off.created', 'professional_time_off.archived',
            'appointment.created', 'appointment.status_changed'
        )
    );

CREATE TABLE atendee.appointments
(
    tenant_id UUID NOT NULL,
    appointment_id UUID NOT NULL,
    customer_id UUID NOT NULL,
    professional_id UUID NOT NULL,
    service_id UUID NOT NULL,
    customer_name_snapshot VARCHAR(120) NOT NULL,
    customer_phone_snapshot VARCHAR(16) NULL,
    professional_name_snapshot VARCHAR(120) NOT NULL,
    service_name_snapshot VARCHAR(120) NOT NULL,
    duration_minutes_snapshot INTEGER NOT NULL,
    price_minor_units_snapshot BIGINT NOT NULL,
    currency_snapshot CHAR(3) NOT NULL,
    buffer_before_minutes_snapshot INTEGER NOT NULL,
    buffer_after_minutes_snapshot INTEGER NOT NULL,
    start_at_utc TIMESTAMPTZ NOT NULL,
    end_at_utc TIMESTAMPTZ NOT NULL,
    occupancy_start_at_utc TIMESTAMPTZ NOT NULL,
    occupancy_end_at_utc TIMESTAMPTZ NOT NULL,
    status SMALLINT NOT NULL,
    idempotency_key VARCHAR(128) NOT NULL,
    request_hash CHAR(64) NOT NULL,
    created_by_identity_user_id UUID NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    cancelled_at_utc TIMESTAMPTZ NULL,
    CONSTRAINT pk_appointments PRIMARY KEY (tenant_id, appointment_id),
    CONSTRAINT uq_appointments_idempotency UNIQUE (tenant_id, idempotency_key),
    CONSTRAINT fk_appointments_customer FOREIGN KEY (tenant_id, customer_id)
        REFERENCES atendee.customers (tenant_id, customer_id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_professional FOREIGN KEY (tenant_id, professional_id)
        REFERENCES atendee.professionals (tenant_id, professional_id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_service FOREIGN KEY (tenant_id, service_id)
        REFERENCES atendee.services (tenant_id, service_id) ON DELETE RESTRICT,
    CONSTRAINT ck_appointments_names CHECK (
        length(btrim(customer_name_snapshot)) BETWEEN 1 AND 120 AND
        length(btrim(professional_name_snapshot)) BETWEEN 1 AND 120 AND
        length(btrim(service_name_snapshot)) BETWEEN 1 AND 120
    ),
    CONSTRAINT ck_appointments_duration CHECK (duration_minutes_snapshot BETWEEN 1 AND 1440),
    CONSTRAINT ck_appointments_price CHECK (price_minor_units_snapshot >= 0),
    CONSTRAINT ck_appointments_currency CHECK (currency_snapshot ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_appointments_buffers CHECK (
        buffer_before_minutes_snapshot BETWEEN 0 AND 1440 AND
        buffer_after_minutes_snapshot BETWEEN 0 AND 1440
    ),
    CONSTRAINT ck_appointments_time_order CHECK (
        start_at_utc < end_at_utc AND
        occupancy_start_at_utc <= start_at_utc AND
        end_at_utc <= occupancy_end_at_utc AND
        end_at_utc - start_at_utc = duration_minutes_snapshot * interval '1 minute' AND
        start_at_utc = date_trunc('minute', start_at_utc)
    ),
    CONSTRAINT ck_appointments_status CHECK (status BETWEEN 1 AND 7),
    CONSTRAINT ck_appointments_idempotency CHECK (
        length(idempotency_key) BETWEEN 16 AND 128 AND
        idempotency_key ~ '^[A-Za-z0-9._:-]+$' AND
        request_hash ~ '^[0-9a-f]{64}$'
    ),
    CONSTRAINT ck_appointments_timestamps CHECK (
        updated_at_utc >= created_at_utc AND
        (cancelled_at_utc IS NULL OR cancelled_at_utc >= created_at_utc)
    )
);

ALTER TABLE atendee.appointments
    ADD CONSTRAINT fk_appointments_creator_membership
        FOREIGN KEY (tenant_id, created_by_identity_user_id)
        REFERENCES atendee.tenant_users (tenant_id, identity_user_id) ON DELETE RESTRICT,
    ADD CONSTRAINT ck_appointments_exact_occupancy CHECK (
        occupancy_start_at_utc = start_at_utc - buffer_before_minutes_snapshot * interval '1 minute' AND
        occupancy_end_at_utc = end_at_utc + buffer_after_minutes_snapshot * interval '1 minute'
    ),
    ADD CONSTRAINT ck_appointments_cancellation_state CHECK (
        ((status = 6) = (cancelled_at_utc IS NOT NULL)) AND
        (cancelled_at_utc IS NULL OR cancelled_at_utc = updated_at_utc)
    );
CREATE INDEX ix_appointments_tenant_start
    ON atendee.appointments (tenant_id, start_at_utc, appointment_id);
CREATE INDEX ix_appointments_tenant_professional_start
    ON atendee.appointments (tenant_id, professional_id, occupancy_start_at_utc)
    WHERE status IN (1, 2, 3, 4);
CREATE INDEX ix_appointments_tenant_customer_start
    ON atendee.appointments (tenant_id, customer_id, start_at_utc DESC);

ALTER TABLE atendee.appointments ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.appointments FORCE ROW LEVEL SECURITY;
CREATE POLICY appointments_runtime_isolation ON atendee.appointments
    FOR ALL TO atendee_runtime
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);
CREATE POLICY appointments_owner_access ON atendee.appointments
    FOR ALL TO atendee_owner USING (true) WITH CHECK (true);

-- Deferred, database-enforced overlap guard. All writers serialize on a stable
-- tenant/professional advisory lock. READ COMMITTED then observes the previous
-- transaction after the lock is acquired, including writers outside the API.
CREATE FUNCTION atendee.reject_appointment_overlap()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
BEGIN
    IF NEW.status NOT IN (1, 2, 3, 4) THEN
        RETURN NEW;
    END IF;

    PERFORM pg_advisory_xact_lock(
        hashtextextended(NEW.tenant_id::text || ':' || NEW.professional_id::text, 0)
    );

    IF EXISTS (
        SELECT 1
        FROM atendee.appointments AS existing
        WHERE existing.tenant_id = NEW.tenant_id
          AND existing.professional_id = NEW.professional_id
          AND existing.appointment_id <> NEW.appointment_id
          AND existing.status IN (1, 2, 3, 4)
          AND tstzrange(existing.occupancy_start_at_utc, existing.occupancy_end_at_utc, '[)')
              && tstzrange(NEW.occupancy_start_at_utc, NEW.occupancy_end_at_utc, '[)')
    ) THEN
        RAISE EXCEPTION 'Professional already has an overlapping appointment'
            USING ERRCODE = '23P01', CONSTRAINT = 'ex_appointments_professional_occupancy';
    END IF;

    RETURN NEW;
END;
$function$;
REVOKE ALL ON FUNCTION atendee.reject_appointment_overlap() FROM PUBLIC;
CREATE CONSTRAINT TRIGGER ct_appointments_no_overlap
    AFTER INSERT OR UPDATE ON atendee.appointments
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION atendee.reject_appointment_overlap();

CREATE TABLE atendee.appointment_events
(
    tenant_id UUID NOT NULL,
    event_id UUID NOT NULL,
    appointment_id UUID NOT NULL,
    event_type VARCHAR(80) NOT NULL,
    from_status SMALLINT NULL,
    to_status SMALLINT NOT NULL,
    actor_identity_user_id UUID NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_appointment_events PRIMARY KEY (tenant_id, event_id),
    CONSTRAINT fk_appointment_events_appointment FOREIGN KEY (tenant_id, appointment_id)
        REFERENCES atendee.appointments (tenant_id, appointment_id) ON DELETE RESTRICT,
    CONSTRAINT ck_appointment_events_type CHECK (event_type IN ('appointment.created', 'appointment.status_changed')),
    CONSTRAINT ck_appointment_events_status CHECK (
        to_status BETWEEN 1 AND 7 AND (from_status IS NULL OR from_status BETWEEN 1 AND 7)
    )
);
ALTER TABLE atendee.appointment_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.appointment_events FORCE ROW LEVEL SECURITY;
CREATE POLICY appointment_events_runtime_read ON atendee.appointment_events
    FOR SELECT TO atendee_runtime
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);
CREATE POLICY appointment_events_owner_access ON atendee.appointment_events
    FOR ALL TO atendee_owner USING (true) WITH CHECK (true);

CREATE TABLE atendee.transactional_outbox
(
    tenant_id UUID NOT NULL,
    message_id UUID NOT NULL,
    aggregate_id UUID NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL,
    available_at_utc TIMESTAMPTZ NOT NULL,
    processed_at_utc TIMESTAMPTZ NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT pk_transactional_outbox PRIMARY KEY (tenant_id, message_id),
    CONSTRAINT ck_transactional_outbox_attempts CHECK (attempts >= 0),
    CONSTRAINT ck_transactional_outbox_type CHECK (event_type ~ '^[a-z][a-z0-9_.-]{2,99}$')
);
CREATE INDEX ix_transactional_outbox_pending
    ON atendee.transactional_outbox (available_at_utc, occurred_at_utc)
    WHERE processed_at_utc IS NULL;
ALTER TABLE atendee.transactional_outbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.transactional_outbox FORCE ROW LEVEL SECURITY;
CREATE POLICY transactional_outbox_owner_access ON atendee.transactional_outbox
    FOR ALL TO atendee_owner USING (true) WITH CHECK (true);

-- Snapshot/event creation and audit are inseparable from the booking transaction.
CREATE FUNCTION atendee.record_appointment_creation()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    actor_text text;
BEGIN
    actor_text := NULLIF(current_setting('app.actor_id', true), '');
    IF actor_text IS NULL OR actor_text::uuid <> NEW.created_by_identity_user_id OR NOT EXISTS (
        SELECT 1 FROM atendee.tenant_users AS membership
        WHERE membership.tenant_id = NEW.tenant_id
          AND membership.identity_user_id = actor_text::uuid
          AND membership.is_active = true
    ) THEN
        RAISE EXCEPTION 'Active trusted appointment actor context is required';
    END IF;

    INSERT INTO atendee.appointment_events
        (tenant_id, event_id, appointment_id, event_type, from_status, to_status, actor_identity_user_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), NEW.appointment_id, 'appointment.created',
         NULL, NEW.status, NEW.created_by_identity_user_id, NEW.created_at_utc);

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), NEW.created_by_identity_user_id,
         'appointment.created', NEW.appointment_id, NEW.created_at_utc);

    INSERT INTO atendee.transactional_outbox
        (tenant_id, message_id, aggregate_id, event_type, payload, occurred_at_utc, available_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), NEW.appointment_id, 'appointment.created',
         jsonb_build_object('appointmentId', NEW.appointment_id, 'professionalId', NEW.professional_id),
         NEW.created_at_utc, NEW.created_at_utc);

    RETURN NEW;
END;
$function$;
REVOKE ALL ON FUNCTION atendee.record_appointment_creation() FROM PUBLIC;
CREATE TRIGGER tr_appointments_record_creation
    AFTER INSERT ON atendee.appointments
    FOR EACH ROW EXECUTE FUNCTION atendee.record_appointment_creation();

CREATE FUNCTION atendee.reject_appointment_deletion()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog, atendee
AS $function$
BEGIN
    RAISE EXCEPTION 'Appointment records and event history cannot be physically removed or truncated';
END;
$function$;
REVOKE ALL ON FUNCTION atendee.reject_appointment_deletion() FROM PUBLIC;
CREATE TRIGGER tr_appointment_events_append_only
    BEFORE UPDATE OR DELETE ON atendee.appointment_events
    FOR EACH ROW EXECUTE FUNCTION atendee.reject_appointment_deletion();
CREATE TRIGGER tr_appointment_events_no_truncate
    BEFORE TRUNCATE ON atendee.appointment_events
    FOR EACH STATEMENT EXECUTE FUNCTION atendee.reject_appointment_deletion();

CREATE TRIGGER tr_appointments_no_delete
    BEFORE DELETE ON atendee.appointments
    FOR EACH ROW EXECUTE FUNCTION atendee.reject_appointment_deletion();
CREATE TRIGGER tr_appointments_no_truncate
    BEFORE TRUNCATE ON atendee.appointments
    FOR EACH STATEMENT EXECUTE FUNCTION atendee.reject_appointment_deletion();-- Booking facts are immutable. Only legal lifecycle transitions may update a row.
CREATE FUNCTION atendee.validate_appointment_transition()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
BEGIN
    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id OR
       NEW.appointment_id IS DISTINCT FROM OLD.appointment_id OR
       NEW.customer_id IS DISTINCT FROM OLD.customer_id OR
       NEW.professional_id IS DISTINCT FROM OLD.professional_id OR
       NEW.service_id IS DISTINCT FROM OLD.service_id OR
       NEW.customer_name_snapshot IS DISTINCT FROM OLD.customer_name_snapshot OR
       NEW.customer_phone_snapshot IS DISTINCT FROM OLD.customer_phone_snapshot OR
       NEW.professional_name_snapshot IS DISTINCT FROM OLD.professional_name_snapshot OR
       NEW.service_name_snapshot IS DISTINCT FROM OLD.service_name_snapshot OR
       NEW.duration_minutes_snapshot IS DISTINCT FROM OLD.duration_minutes_snapshot OR
       NEW.price_minor_units_snapshot IS DISTINCT FROM OLD.price_minor_units_snapshot OR
       NEW.currency_snapshot IS DISTINCT FROM OLD.currency_snapshot OR
       NEW.buffer_before_minutes_snapshot IS DISTINCT FROM OLD.buffer_before_minutes_snapshot OR
       NEW.buffer_after_minutes_snapshot IS DISTINCT FROM OLD.buffer_after_minutes_snapshot OR
       NEW.start_at_utc IS DISTINCT FROM OLD.start_at_utc OR
       NEW.end_at_utc IS DISTINCT FROM OLD.end_at_utc OR
       NEW.occupancy_start_at_utc IS DISTINCT FROM OLD.occupancy_start_at_utc OR
       NEW.occupancy_end_at_utc IS DISTINCT FROM OLD.occupancy_end_at_utc OR
       NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key OR
       NEW.request_hash IS DISTINCT FROM OLD.request_hash OR
       NEW.created_by_identity_user_id IS DISTINCT FROM OLD.created_by_identity_user_id OR
       NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc THEN
        RAISE EXCEPTION 'Appointment booking snapshots are immutable'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_appointments_immutable_snapshots';
    END IF;

    IF NEW.updated_at_utc < OLD.updated_at_utc OR NEW.updated_at_utc < OLD.created_at_utc THEN
        RAISE EXCEPTION 'Appointment update timestamp cannot move backwards'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_appointments_timestamps';
    END IF;

    IF NOT (
        (OLD.status = 1 AND NEW.status IN (2, 6)) OR
        (OLD.status = 2 AND NEW.status IN (3, 6, 7)) OR
        (OLD.status = 3 AND NEW.status = 4) OR
        (OLD.status = 4 AND NEW.status = 5)
    ) THEN
        RAISE EXCEPTION 'Illegal appointment status transition'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_appointments_status_transition';
    END IF;

    IF NEW.status = 6 THEN
        IF NEW.cancelled_at_utc IS DISTINCT FROM NEW.updated_at_utc THEN
            RAISE EXCEPTION 'Cancellation timestamp must equal the transition timestamp'
                USING ERRCODE = '23514', CONSTRAINT = 'ck_appointments_cancelled_at';
        END IF;
    ELSIF NEW.cancelled_at_utc IS DISTINCT FROM OLD.cancelled_at_utc THEN
        RAISE EXCEPTION 'Cancellation timestamp is immutable after cancellation'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_appointments_cancelled_at';
    END IF;

    RETURN NEW;
END;
$function$;
REVOKE ALL ON FUNCTION atendee.validate_appointment_transition() FROM PUBLIC;
CREATE TRIGGER tr_appointments_validate_transition
    BEFORE UPDATE ON atendee.appointments
    FOR EACH ROW EXECUTE FUNCTION atendee.validate_appointment_transition();

CREATE FUNCTION atendee.record_appointment_status_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    actor_text text;
BEGIN
    actor_text := NULLIF(current_setting('app.actor_id', true), '');
    IF actor_text IS NULL OR NOT EXISTS (
        SELECT 1 FROM atendee.tenant_users AS membership
        WHERE membership.tenant_id = NEW.tenant_id
          AND membership.identity_user_id = actor_text::uuid
          AND membership.is_active = true
    ) THEN
        RAISE EXCEPTION 'An active trusted tenant actor is required for appointment transitions';
    END IF;

    INSERT INTO atendee.appointment_events
        (tenant_id, event_id, appointment_id, event_type, from_status, to_status, actor_identity_user_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), NEW.appointment_id, 'appointment.status_changed',
         OLD.status, NEW.status, actor_text::uuid, NEW.updated_at_utc);

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid,
         'appointment.status_changed', NEW.appointment_id, NEW.updated_at_utc);

    INSERT INTO atendee.transactional_outbox
        (tenant_id, message_id, aggregate_id, event_type, payload, occurred_at_utc, available_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), NEW.appointment_id, 'appointment.status_changed',
         jsonb_build_object('appointmentId', NEW.appointment_id, 'fromStatus', OLD.status, 'toStatus', NEW.status),
         NEW.updated_at_utc, NEW.updated_at_utc);

    RETURN NEW;
END;
$function$;
REVOKE ALL ON FUNCTION atendee.record_appointment_status_change() FROM PUBLIC;
CREATE TRIGGER tr_appointments_record_status_change
    AFTER UPDATE OF status ON atendee.appointments
    FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status)
    EXECUTE FUNCTION atendee.record_appointment_status_change();
GRANT SELECT, INSERT, UPDATE ON atendee.appointments TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.appointments FROM atendee_runtime, PUBLIC;

GRANT SELECT ON atendee.appointment_events TO atendee_runtime;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON atendee.appointment_events FROM atendee_runtime, PUBLIC;
GRANT INSERT ON atendee.transactional_outbox TO atendee_runtime;
REVOKE INSERT, SELECT, UPDATE, DELETE, TRUNCATE ON atendee.transactional_outbox FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;