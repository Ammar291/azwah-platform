-- Database roles for a deployed environment (D-86). Run once per database by a database administrator, then give
-- each role its password from the secret store, for example:
--   ALTER ROLE azwah_migrator PASSWORD '<from the secret store>';
--   ALTER ROLE azwah_app PASSWORD '<from the secret store>';
-- azwah_migrator owns the schema and runs migrations (the migration bundle, or ConnectionStrings:Migrations).
-- azwah_app is the API's role (ConnectionStrings:Default): data changes only, granted by application-role.sql after
-- every migration. Idempotent. No passwords or secrets in this file.

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'azwah_migrator') THEN
        CREATE ROLE azwah_migrator LOGIN;
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'azwah_app') THEN
        CREATE ROLE azwah_app LOGIN;
    END IF;

    -- CREATE on the database lets the migrator add the trusted extension the migrations need (btree_gist).
    EXECUTE format('GRANT CONNECT, CREATE ON DATABASE %I TO azwah_migrator', current_database());
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO azwah_app', current_database());
END
$$;

-- Nobody but the migrator creates objects in the schema.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO azwah_migrator;
GRANT USAGE ON SCHEMA public TO azwah_app;
