using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written (the model did not change): after insert, a safety alert's ID, subject (user, household, member),
    /// source, concern, severity, first detection and session never change; what the domain changes (the count, the latest
    /// detection and its detector and code, the status and who acknowledged or resolved it) stays free. Alerts are deleted
    /// only by household erasure (<c>azwah.erasure</c>). Like the other guards this is not a security boundary against a
    /// database owner. Never regenerate.
    /// </summary>
    public partial class AddSafetyAlertGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_safety_alerts_guarded() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        -- Reserved for the erasure procedure (docs/database-conventions.md), which sets this per transaction.
                        IF current_setting('azwah.erasure', true) = 'on' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Safety alerts are deleted only by the erasure procedure.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF (NEW.id, NEW.platform_user_id, NEW.household_id, NEW.family_member_id, NEW.source, NEW.concern,
                        NEW.severity, NEW.first_detected_at, NEW.assessment_session_id)
                       IS DISTINCT FROM
                       (OLD.id, OLD.platform_user_id, OLD.household_id, OLD.family_member_id, OLD.source, OLD.concern,
                        OLD.severity, OLD.first_detected_at, OLD.assessment_session_id)
                    THEN
                        RAISE EXCEPTION 'A safety alert''s subject, source, concern, severity and first detection never change.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_safety_alerts_guarded
                    BEFORE UPDATE OR DELETE ON safety_alerts
                    FOR EACH ROW EXECUTE FUNCTION azwah_safety_alerts_guarded();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_safety_alerts_guarded ON safety_alerts;
                DROP FUNCTION IF EXISTS azwah_safety_alerts_guarded();
                """);
        }
    }
}
