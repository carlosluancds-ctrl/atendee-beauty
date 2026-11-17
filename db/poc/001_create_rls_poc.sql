-- ATENÇÃO: executar SOMENTE em um banco PostgreSQL descartável de teste.
-- Criar papéis sem LOGIN. O papel runtime não pode ser dono da tabela,
-- superuser, BYPASSRLS nem membro do papel owner.
-- Este arquivo é POC, não é migração de produção.

DO $preflight$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname IN ('atendee_poc_owner', 'atendee_poc_runtime')) THEN
        RAISE EXCEPTION 'POC roles already exist. Use a fresh disposable PostgreSQL cluster/container.';
    END IF;
END
$preflight$;

CREATE ROLE atendee_poc_owner
    NOLOGIN NOINHERIT NOBYPASSRLS;
CREATE ROLE atendee_poc_runtime
    NOLOGIN NOINHERIT NOBYPASSRLS;

CREATE SCHEMA atendee_rls_poc AUTHORIZATION atendee_poc_owner;
REVOKE ALL ON SCHEMA atendee_rls_poc FROM PUBLIC;
GRANT USAGE ON SCHEMA atendee_rls_poc TO atendee_poc_runtime;

ALTER DEFAULT PRIVILEGES FOR ROLE atendee_poc_owner IN SCHEMA atendee_rls_poc
    REVOKE ALL ON TABLES FROM PUBLIC;

SET ROLE atendee_poc_owner;

CREATE TABLE atendee_rls_poc.records
(
    tenant_id UUID NOT NULL,
    record_id UUID NOT NULL,
    payload TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_records PRIMARY KEY (tenant_id, record_id)
);

RESET ROLE;

REVOKE ALL ON TABLE atendee_rls_poc.records FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE
    ON TABLE atendee_rls_poc.records
    TO atendee_poc_runtime;

ALTER TABLE atendee_rls_poc.records ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee_rls_poc.records FORCE ROW LEVEL SECURITY;

-- O tenant deve ser resolvido no servidor a partir da identidade autenticada.
-- Nunca aceite tenant_id do navegador. Este GUC é contexto, não credencial:
-- SQL injection/acesso SQL arbitrário ainda pode tentar alterar set_config().
CREATE POLICY tenant_isolation_policy
ON atendee_rls_poc.records
FOR ALL
TO atendee_poc_runtime
USING (
    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID
)
WITH CHECK (
    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID
);

CREATE INDEX ix_records_tenant_created
    ON atendee_rls_poc.records (tenant_id, created_at_utc DESC);

-- Confirmação de configuração; valores examinados pelo teste seguinte.
SELECT
    n.nspname AS schema_name,
    c.relname AS table_name,
    pg_get_userbyid(c.relowner) AS table_owner,
    c.relrowsecurity AS rls_enabled,
    c.relforcerowsecurity AS rls_forced
FROM pg_class AS c
JOIN pg_namespace AS n ON n.oid = c.relnamespace
WHERE n.nspname = 'atendee_rls_poc'
  AND c.relname = 'records';
