-- Initial tenant-scoped persistence table. Run as atendee_migrator on a fresh
-- database after db/security/001_roles_template.sql has been applied.
-- This is a sample persistence slice, not the complete production schema.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_owner;

CREATE TABLE atendee.tenant_notes
(
    tenant_id UUID NOT NULL,
    note_id UUID NOT NULL,
    body VARCHAR(1000) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_tenant_notes PRIMARY KEY (tenant_id, note_id),
    CONSTRAINT ck_tenant_notes_body_nonempty CHECK (length(btrim(body)) > 0)
);

CREATE INDEX ix_tenant_notes_tenant_created
    ON atendee.tenant_notes (tenant_id, created_at_utc DESC);

ALTER TABLE atendee.tenant_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.tenant_notes FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_notes_isolation
ON atendee.tenant_notes
FOR ALL
TO atendee_runtime
USING (
    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID
)
WITH CHECK (
    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID
);

COMMIT;

-- Verify owner, runtime privileges and RLS flags separately before deployment.
