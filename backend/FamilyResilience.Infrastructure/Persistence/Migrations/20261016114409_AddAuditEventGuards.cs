using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written: audit events were append-only against UPDATE only (AddDatabaseGuards). Now DELETE is refused unless
    /// the transaction sets <c>azwah.audit_retention = 'on'</c>, which is reserved for the retention job that deletes
    /// events past an approved period (none is approved yet; docs/retention-policy.md), and TRUNCATE is always refused.
    /// The erasure setting does not open them: erasure keeps audit events, which hold opaque IDs only. Like the other
    /// guards this is not a security boundary against a database owner; the application role's grants are
    /// (docs/security-model.md). Never regenerate.
    /// </summary>
    public partial class AddAuditEventGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_audit_events_guarded() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('azwah.audit_retention', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'Rows in % are append-only.', TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_audit_events_no_delete
                    BEFORE DELETE ON audit_events
                    FOR EACH ROW EXECUTE FUNCTION azwah_audit_events_guarded();

                CREATE TRIGGER trg_audit_events_no_truncate
                    BEFORE TRUNCATE ON audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION azwah_audit_events_guarded();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_events_no_truncate ON audit_events;
                DROP TRIGGER IF EXISTS trg_audit_events_no_delete ON audit_events;
                DROP FUNCTION IF EXISTS azwah_audit_events_guarded();
                """);
        }
    }
}
