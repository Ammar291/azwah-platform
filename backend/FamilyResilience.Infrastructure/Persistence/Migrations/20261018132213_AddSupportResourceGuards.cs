using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written (the model did not change): an approved support resource may only become retired, and a retired one
    /// never changes. Drafts change and delete freely. The generated <c>search_vector</c> is derived from the content
    /// columns, so it is not compared. Like the other guards this is not a security boundary against a database owner.
    /// Never regenerate.
    /// </summary>
    public partial class AddSupportResourceGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_support_resources_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 'Draft' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Approved and retired support resources cannot be deleted.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 'Draft' THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.status = 'Approved'
                       AND NEW.status IN ('Approved', 'Retired')
                       AND (NEW.id, NEW.code, NEW.version_number, NEW.visibility, NEW.name_ar, NEW.name_en,
                            NEW.summary_ar, NEW.summary_en, NEW.provider_name_ar, NEW.provider_name_en, NEW.url,
                            NEW.categories, NEW.audiences, NEW.service_languages, NEW.approved_by, NEW.approved_at,
                            NEW.approval_source, NEW.review_due_at, NEW.created_at)
                           IS NOT DISTINCT FROM
                           (OLD.id, OLD.code, OLD.version_number, OLD.visibility, OLD.name_ar, OLD.name_en,
                            OLD.summary_ar, OLD.summary_en, OLD.provider_name_ar, OLD.provider_name_en, OLD.url,
                            OLD.categories, OLD.audiences, OLD.service_languages, OLD.approved_by, OLD.approved_at,
                            OLD.approval_source, OLD.review_due_at, OLD.created_at)
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Approved and retired support resources cannot be changed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_support_resources_immutable
                    BEFORE UPDATE OR DELETE ON support_resources
                    FOR EACH ROW EXECUTE FUNCTION azwah_support_resources_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_support_resources_immutable ON support_resources;
                DROP FUNCTION IF EXISTS azwah_support_resources_immutable();
                """);
        }
    }
}
