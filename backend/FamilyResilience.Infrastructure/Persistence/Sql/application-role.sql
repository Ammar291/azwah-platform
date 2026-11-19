-- What the API's role (azwah_app) may do (D-86). Run by the schema owner (azwah_migrator) after every migration;
-- idempotent. The API reads and changes rows; it never changes the schema, and audit events are append-only for it:
-- it may add and read them, never update, delete or truncate them. The database guards (triggers) apply on top.

GRANT USAGE ON SCHEMA public TO azwah_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO azwah_app;
REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON audit_events FROM azwah_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON "__EFMigrationsHistory" FROM azwah_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO azwah_app;

-- Tables a later migration adds get the same data permissions; audit-like tables must be revoked here like above.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO azwah_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO azwah_app;
