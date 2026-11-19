using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written guards for the assessment engine: score results, contributions and category scores never change once
    /// written, and a version cannot move from Draft to Approved while any text of it or its instrument lacks Arabic.
    /// Never regenerate.
    /// </summary>
    public partial class AddAssessmentEngineGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_score_results_no_update
                    BEFORE UPDATE ON score_results
                    FOR EACH ROW EXECUTE FUNCTION azwah_forbid_update();
                CREATE TRIGGER trg_score_contributions_no_update
                    BEFORE UPDATE ON score_contributions
                    FOR EACH ROW EXECUTE FUNCTION azwah_forbid_update();
                CREATE TRIGGER trg_score_category_scores_no_update
                    BEFORE UPDATE ON score_category_scores
                    FOR EACH ROW EXECUTE FUNCTION azwah_forbid_update();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_instrument_versions_bilingual_on_approval() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status = 'Draft' AND NEW.status = 'Approved' AND (
                        EXISTS (SELECT 1 FROM instruments i
                                WHERE i.id = NEW.instrument_id AND (i.name_ar IS NULL OR i.description_ar IS NULL))
                        OR EXISTS (SELECT 1 FROM instrument_sections s
                                   WHERE s.instrument_version_id = NEW.id AND s.title_ar IS NULL)
                        OR EXISTS (SELECT 1 FROM instrument_items it
                                   WHERE it.instrument_version_id = NEW.id AND it.prompt_ar IS NULL)
                        OR EXISTS (SELECT 1 FROM response_options o
                                   JOIN instrument_items it ON it.id = o.instrument_item_id
                                   WHERE it.instrument_version_id = NEW.id AND o.label_ar IS NULL))
                    THEN
                        RAISE EXCEPTION 'An instrument version cannot be approved while any of its text lacks Arabic.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_instrument_versions_bilingual_on_approval
                    BEFORE UPDATE ON instrument_versions
                    FOR EACH ROW EXECUTE FUNCTION azwah_instrument_versions_bilingual_on_approval();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_instrument_versions_bilingual_on_approval ON instrument_versions;
                DROP FUNCTION IF EXISTS azwah_instrument_versions_bilingual_on_approval();
                DROP TRIGGER IF EXISTS trg_score_category_scores_no_update ON score_category_scores;
                DROP TRIGGER IF EXISTS trg_score_contributions_no_update ON score_contributions;
                DROP TRIGGER IF EXISTS trg_score_results_no_update ON score_results;
                """);
        }
    }
}
