\set ON_ERROR_STOP on
-- Executar SOMENTE no banco descartável criado pelo script 001.
-- O teste usa dados sintéticos e faz ROLLBACK ao final.

BEGIN;
SET LOCAL ROLE atendee_poc_runtime;

DO $poc$
DECLARE
    tenant_a CONSTANT UUID := 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    tenant_b CONSTANT UUID := 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    record_a CONSTANT UUID := '11111111-1111-4111-8111-111111111111';
    record_b CONSTANT UUID := '22222222-2222-4222-8222-222222222222';
    visible_rows BIGINT;
    affected_rows BIGINT;
    owner_name TEXT;
    is_enabled BOOLEAN;
    is_forced BOOLEAN;
    policy_count BIGINT;
    runtime_super BOOLEAN;
    runtime_bypass BOOLEAN;
    runtime_login BOOLEAN;
    runtime_inherit BOOLEAN;
BEGIN
    SELECT rolsuper, rolbypassrls, rolcanlogin, rolinherit
      INTO runtime_super, runtime_bypass, runtime_login, runtime_inherit
    FROM pg_roles
    WHERE rolname = current_user;

    IF runtime_super OR runtime_bypass OR runtime_login OR runtime_inherit THEN
        RAISE EXCEPTION 'Runtime role must be NOLOGIN, NOINHERIT, NOSUPERUSER and NOBYPASSRLS';
    END IF;

    IF pg_has_role('atendee_poc_runtime', 'atendee_poc_owner', 'MEMBER') THEN
        RAISE EXCEPTION 'Runtime role must not be a member of the table-owner role';
    END IF;

    SELECT pg_get_userbyid(c.relowner), c.relrowsecurity, c.relforcerowsecurity
      INTO owner_name, is_enabled, is_forced
    FROM pg_class AS c
    JOIN pg_namespace AS n ON n.oid = c.relnamespace
    WHERE n.nspname = 'atendee_rls_poc' AND c.relname = 'records';

    IF owner_name IS NULL THEN
        RAISE EXCEPTION 'POC table is missing';
    END IF;
    IF owner_name = current_user THEN
        RAISE EXCEPTION 'Runtime role owns the tenant table';
    END IF;
    IF NOT is_enabled OR NOT is_forced THEN
        RAISE EXCEPTION 'RLS is not enabled and forced';
    END IF;

    SELECT count(*) INTO policy_count
    FROM pg_policies
    WHERE schemaname = 'atendee_rls_poc'
      AND tablename = 'records';
    IF policy_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one RLS policy on the POC table, got %', policy_count;
    END IF;

    -- Fail closed without tenant context.
    PERFORM set_config('app.tenant_id', '', true);
    SELECT count(*) INTO visible_rows FROM atendee_rls_poc.records;
    IF visible_rows <> 0 THEN
        RAISE EXCEPTION 'Rows visible without tenant context: %', visible_rows;
    END IF;

    BEGIN
        INSERT INTO atendee_rls_poc.records (tenant_id, record_id, payload)
        VALUES (tenant_a, record_a, 'insert without tenant context');
        RAISE EXCEPTION 'Expected no-context INSERT to be rejected';
    EXCEPTION WHEN insufficient_privilege THEN
        NULL;
    END;

    -- Tenant A may insert only A's row.
    PERFORM set_config('app.tenant_id', tenant_a::TEXT, true);
    INSERT INTO atendee_rls_poc.records (tenant_id, record_id, payload)
    VALUES (tenant_a, record_a, 'synthetic tenant A');

    BEGIN
        INSERT INTO atendee_rls_poc.records (tenant_id, record_id, payload)
        VALUES (tenant_b, record_b, 'forbidden by tenant A');
        RAISE EXCEPTION 'Expected cross-tenant INSERT to be rejected';
    EXCEPTION WHEN insufficient_privilege THEN
        NULL;
    END;

    -- Only an explicit B context may insert B's row.
    PERFORM set_config('app.tenant_id', tenant_b::TEXT, true);
    INSERT INTO atendee_rls_poc.records (tenant_id, record_id, payload)
    VALUES (tenant_b, record_b, 'synthetic tenant B');

    -- Return to A and verify B is hidden even when its UUID is known.
    PERFORM set_config('app.tenant_id', tenant_a::TEXT, true);
    SELECT count(*) INTO visible_rows FROM atendee_rls_poc.records;
    IF visible_rows <> 1 THEN
        RAISE EXCEPTION 'Tenant A expected 1 visible row, got %', visible_rows;
    END IF;

    SELECT count(*) INTO visible_rows
    FROM atendee_rls_poc.records
    WHERE tenant_id = tenant_b AND record_id = record_b;
    IF visible_rows <> 0 THEN
        RAISE EXCEPTION 'Tenant A can read tenant B record';
    END IF;

    UPDATE atendee_rls_poc.records
       SET payload = 'tampered by A'
     WHERE tenant_id = tenant_b AND record_id = record_b;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    IF affected_rows <> 0 THEN
        RAISE EXCEPTION 'Tenant A updated tenant B record';
    END IF;

    DELETE FROM atendee_rls_poc.records
     WHERE tenant_id = tenant_b AND record_id = record_b;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    IF affected_rows <> 0 THEN
        RAISE EXCEPTION 'Tenant A deleted tenant B record';
    END IF;

    -- WITH CHECK must stop A from moving its own row into B's tenant.
    BEGIN
        UPDATE atendee_rls_poc.records
           SET tenant_id = tenant_b
         WHERE tenant_id = tenant_a AND record_id = record_a;
        RAISE EXCEPTION 'Expected cross-tenant UPDATE/tenant reassignment to be rejected';
    EXCEPTION WHEN insufficient_privilege THEN
        NULL;
    END;

    -- Own-row update and delete must remain available.
    UPDATE atendee_rls_poc.records
       SET payload = 'updated by A'
     WHERE tenant_id = tenant_a AND record_id = record_a;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    IF affected_rows <> 1 THEN
        RAISE EXCEPTION 'Tenant A expected to update own row exactly once, got %', affected_rows;
    END IF;

    DELETE FROM atendee_rls_poc.records
     WHERE tenant_id = tenant_a AND record_id = record_a;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    IF affected_rows <> 1 THEN
        RAISE EXCEPTION 'Tenant A expected to delete own row exactly once, got %', affected_rows;
    END IF;

    PERFORM set_config('app.tenant_id', tenant_b::TEXT, true);
    SELECT count(*) INTO visible_rows FROM atendee_rls_poc.records;
    IF visible_rows <> 1 THEN
        RAISE EXCEPTION 'Tenant B expected its own row after A deletion, got %', visible_rows;
    END IF;

    RAISE NOTICE 'PASS: role hardening, RLS enabled/forced, single policy, no-context deny, per-tenant visibility, INSERT/UPDATE/DELETE isolation and allowed own-row operations';
END
$poc$;

ROLLBACK;

-- A second transaction in the same session checks that SET LOCAL / set_config
-- context from the previous transaction did not remain attached to the session.
BEGIN;
SET LOCAL ROLE atendee_poc_runtime;
DO $context_reset$
DECLARE
    context_value TEXT;
    visible_rows BIGINT;
BEGIN
    context_value := current_setting('app.tenant_id', true);
    IF context_value IS NOT NULL AND context_value <> '' THEN
        RAISE EXCEPTION 'Tenant context leaked across transaction boundary: %', context_value;
    END IF;

    SELECT count(*) INTO visible_rows FROM atendee_rls_poc.records;
    IF visible_rows <> 0 THEN
        RAISE EXCEPTION 'Rows visible in fresh transaction without tenant context: %', visible_rows;
    END IF;

    RAISE NOTICE 'PASS: transaction-local tenant context did not leak into the next transaction';
END
$context_reset$;
ROLLBACK;
