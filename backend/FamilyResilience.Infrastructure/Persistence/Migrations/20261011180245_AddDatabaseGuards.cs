using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-side guards that EF Core cannot model: the effective-window exclusion constraint and the triggers that
    /// keep approved content, submitted answers, session copies and audit events from changing. Hand-written; never
    /// regenerate.
    /// </summary>
    public partial class AddDatabaseGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.Sql(
                """
                ALTER TABLE instrument_versions
                    ADD CONSTRAINT ex_instrument_versions_effective_window
                    EXCLUDE USING gist (instrument_id WITH =, tstzrange(effective_from, effective_to, '[)') WITH &&)
                    WHERE (status IN ('Approved', 'Retired'));
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_instrument_versions_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 'Draft' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Approved and retired instrument versions cannot be deleted.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 'Draft' THEN
                        RETURN NEW;
                    END IF;

                    -- An approved version may only be retired: status, the end of an open window, retired_at, updated_at.
                    IF OLD.status = 'Approved'
                       AND NEW.status IN ('Approved', 'Retired')
                       AND (OLD.effective_to IS NULL OR NEW.effective_to IS NOT DISTINCT FROM OLD.effective_to)
                       AND (NEW.id, NEW.instrument_id, NEW.version_number, NEW.content_reference_ar, NEW.content_reference_en,
                            NEW.approved_by, NEW.approved_at, NEW.approval_source, NEW.effective_from, NEW.created_at)
                           IS NOT DISTINCT FROM
                           (OLD.id, OLD.instrument_id, OLD.version_number, OLD.content_reference_ar, OLD.content_reference_en,
                            OLD.approved_by, OLD.approved_at, OLD.approval_source, OLD.effective_from, OLD.created_at)
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Approved and retired instrument versions cannot be changed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_instrument_versions_immutable
                    BEFORE UPDATE OR DELETE ON instrument_versions
                    FOR EACH ROW EXECUTE FUNCTION azwah_instrument_versions_immutable();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_instrument_content_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    old_version uuid;
                    new_version uuid;
                    version_status text;
                BEGIN
                    -- The version a row belongs to before (UPDATE, DELETE) and after (INSERT, UPDATE) the statement. An
                    -- UPDATE can move a row between versions, so both ends must be drafts.
                    IF TG_TABLE_NAME = 'response_options' THEN
                        IF TG_OP IN ('UPDATE', 'DELETE') THEN
                            SELECT instrument_version_id INTO old_version FROM instrument_items WHERE id = OLD.instrument_item_id;
                        END IF;
                        IF TG_OP IN ('INSERT', 'UPDATE') THEN
                            SELECT instrument_version_id INTO new_version FROM instrument_items WHERE id = NEW.instrument_item_id;
                        END IF;
                    ELSE
                        IF TG_OP IN ('UPDATE', 'DELETE') THEN
                            old_version := OLD.instrument_version_id;
                        END IF;
                        IF TG_OP IN ('INSERT', 'UPDATE') THEN
                            new_version := NEW.instrument_version_id;
                        END IF;
                    END IF;

                    -- No version row means the version itself is being deleted, which the version trigger allows only for drafts.
                    IF old_version IS NOT NULL THEN
                        SELECT status INTO version_status FROM instrument_versions WHERE id = old_version;
                        IF version_status IS NOT NULL AND version_status <> 'Draft' THEN
                            RAISE EXCEPTION 'Content of approved and retired instrument versions cannot be changed.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                    END IF;

                    IF new_version IS NOT NULL THEN
                        SELECT status INTO version_status FROM instrument_versions WHERE id = new_version;
                        IF version_status IS NOT NULL AND version_status <> 'Draft' THEN
                            RAISE EXCEPTION 'Content of approved and retired instrument versions cannot be changed.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_instrument_content_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON instrument_sections
                    FOR EACH ROW EXECUTE FUNCTION azwah_instrument_content_immutable();
                CREATE TRIGGER trg_instrument_content_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON instrument_items
                    FOR EACH ROW EXECUTE FUNCTION azwah_instrument_content_immutable();
                CREATE TRIGGER trg_instrument_content_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON response_options
                    FOR EACH ROW EXECUTE FUNCTION azwah_instrument_content_immutable();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_rule_sets_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 'Proposed' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Approved and retired rule sets cannot be deleted.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 'Proposed' THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.status = 'Approved'
                       AND NEW.status IN ('Approved', 'Retired')
                       AND (NEW.id, NEW.kind, NEW.code, NEW.version_number, NEW.instrument_version_id, NEW.definition,
                            NEW.approved_by, NEW.approved_at, NEW.approval_source, NEW.created_at)
                           IS NOT DISTINCT FROM
                           (OLD.id, OLD.kind, OLD.code, OLD.version_number, OLD.instrument_version_id, OLD.definition,
                            OLD.approved_by, OLD.approved_at, OLD.approval_source, OLD.created_at)
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Approved and retired rule sets cannot be changed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_rule_sets_immutable
                    BEFORE UPDATE OR DELETE ON rule_sets
                    FOR EACH ROW EXECUTE FUNCTION azwah_rule_sets_immutable();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_consent_policy_versions_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 'Draft' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Approved and retired consent policies cannot be deleted.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 'Draft' THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.status = 'Approved'
                       AND NEW.status IN ('Approved', 'Retired')
                       AND (NEW.id, NEW.consent_type, NEW.version_number, NEW.document_reference_ar, NEW.document_reference_en,
                            NEW.approved_by, NEW.approved_at, NEW.approval_source, NEW.created_at)
                           IS NOT DISTINCT FROM
                           (OLD.id, OLD.consent_type, OLD.version_number, OLD.document_reference_ar, OLD.document_reference_en,
                            OLD.approved_by, OLD.approved_at, OLD.approval_source, OLD.created_at)
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Approved and retired consent policies cannot be changed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_consent_policy_versions_immutable
                    BEFORE UPDATE OR DELETE ON consent_policy_versions
                    FOR EACH ROW EXECUTE FUNCTION azwah_consent_policy_versions_immutable();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_assessment_responses_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    session_status text;
                BEGIN
                    -- Reserved for the erasure procedure (docs/database-conventions.md), which sets this per transaction.
                    IF TG_OP = 'DELETE' AND current_setting('azwah.erasure', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    -- An UPDATE can move a response between sessions, so both the old and the new session must be open.
                    IF TG_OP IN ('UPDATE', 'DELETE') THEN
                        SELECT status INTO session_status FROM assessment_sessions WHERE id = OLD.assessment_session_id;
                        IF session_status IS DISTINCT FROM 'InProgress' THEN
                            RAISE EXCEPTION 'Responses can change only while their session is in progress.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                    END IF;

                    IF TG_OP IN ('INSERT', 'UPDATE') THEN
                        SELECT status INTO session_status FROM assessment_sessions WHERE id = NEW.assessment_session_id;
                        IF session_status IS DISTINCT FROM 'InProgress' THEN
                            RAISE EXCEPTION 'Responses can change only while their session is in progress.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_assessment_responses_frozen
                    BEFORE INSERT OR UPDATE OR DELETE ON assessment_responses
                    FOR EACH ROW EXECUTE FUNCTION azwah_assessment_responses_frozen();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_assessment_sessions_guarded() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    assigned_section uuid;
                BEGIN
                    -- The composite foreign key pins the member and version copies; the section is pinned here. A
                    -- missing assignment is left to that foreign key, which reports it with its own name.
                    IF TG_OP = 'INSERT' THEN
                        SELECT instrument_section_id INTO assigned_section
                            FROM assessment_assignments WHERE id = NEW.assessment_assignment_id;
                        IF FOUND AND NEW.instrument_section_id IS DISTINCT FROM assigned_section THEN
                            RAISE EXCEPTION 'A session must copy the section of its assignment.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF (NEW.assessment_assignment_id, NEW.family_member_id, NEW.instrument_version_id,
                        NEW.instrument_section_id, NEW.started_at)
                       IS DISTINCT FROM
                       (OLD.assessment_assignment_id, OLD.family_member_id, OLD.instrument_version_id,
                        OLD.instrument_section_id, OLD.started_at)
                    THEN
                        RAISE EXCEPTION 'A session keeps its assignment, member, version, section and start time.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    -- Submission and abandonment times are set once; re-scoring a scored session moves scored_at forward.
                    IF (OLD.submitted_at IS NOT NULL AND NEW.submitted_at IS DISTINCT FROM OLD.submitted_at)
                       OR (OLD.abandoned_at IS NOT NULL AND NEW.abandoned_at IS DISTINCT FROM OLD.abandoned_at)
                       OR (OLD.scored_at IS NOT NULL
                           AND NEW.scored_at IS DISTINCT FROM OLD.scored_at
                           AND NOT (OLD.status = 'Scored' AND COALESCE(NEW.scored_at > OLD.scored_at, false)))
                    THEN
                        RAISE EXCEPTION 'Session submission, scoring and abandonment times cannot be rewritten.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    -- Sessions only move forward; nothing leaves Abandoned.
                    IF (OLD.status = 'InProgress' AND NEW.status IN ('InProgress', 'Submitted', 'Abandoned'))
                       OR (OLD.status = 'Submitted' AND NEW.status IN ('Submitted', 'Scored'))
                       OR (OLD.status = 'Scored' AND NEW.status = 'Scored')
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'This session status change is not allowed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_assessment_sessions_guarded
                    BEFORE INSERT OR UPDATE ON assessment_sessions
                    FOR EACH ROW EXECUTE FUNCTION azwah_assessment_sessions_guarded();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_forbid_update() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Rows in % are append-only.', TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_audit_events_no_update
                    BEFORE UPDATE ON audit_events
                    FOR EACH ROW EXECUTE FUNCTION azwah_forbid_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_events_no_update ON audit_events;
                DROP FUNCTION IF EXISTS azwah_forbid_update();
                DROP TRIGGER IF EXISTS trg_assessment_sessions_guarded ON assessment_sessions;
                DROP FUNCTION IF EXISTS azwah_assessment_sessions_guarded();
                DROP TRIGGER IF EXISTS trg_assessment_responses_frozen ON assessment_responses;
                DROP FUNCTION IF EXISTS azwah_assessment_responses_frozen();
                DROP TRIGGER IF EXISTS trg_consent_policy_versions_immutable ON consent_policy_versions;
                DROP FUNCTION IF EXISTS azwah_consent_policy_versions_immutable();
                DROP TRIGGER IF EXISTS trg_rule_sets_immutable ON rule_sets;
                DROP FUNCTION IF EXISTS azwah_rule_sets_immutable();
                DROP TRIGGER IF EXISTS trg_instrument_content_immutable ON response_options;
                DROP TRIGGER IF EXISTS trg_instrument_content_immutable ON instrument_items;
                DROP TRIGGER IF EXISTS trg_instrument_content_immutable ON instrument_sections;
                DROP FUNCTION IF EXISTS azwah_instrument_content_immutable();
                DROP TRIGGER IF EXISTS trg_instrument_versions_immutable ON instrument_versions;
                DROP FUNCTION IF EXISTS azwah_instrument_versions_immutable();
                ALTER TABLE instrument_versions DROP CONSTRAINT IF EXISTS ex_instrument_versions_effective_window;
                DROP EXTENSION IF EXISTS btree_gist;
                """);
        }
    }
}
