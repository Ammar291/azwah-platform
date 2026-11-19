using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hand-written guard for support referrals: a referral starts open, from a scored session, an approved referral rule
    /// set and the member's active referral consent (read under a share lock, so a concurrent withdrawal waits); it is
    /// assigned to a service only from Open and only while that consent is still active; it keeps its copied keys, its
    /// category and its service; it only moves forward; and it is deleted only by the erasure procedure. Never regenerate.
    /// </summary>
    public partial class AddSupportReferralGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION azwah_support_referrals_guarded() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    session_status text;
                    rules_kind text;
                    rules_status text;
                    consent_kind text;
                    consent_withdrawn_at timestamptz;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        -- Reserved for the erasure procedure (docs/database-conventions.md), which sets this per transaction.
                        IF current_setting('azwah.erasure', true) = 'on' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'Support referrals are deleted only by the erasure procedure.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status <> 'Open' THEN
                            RAISE EXCEPTION 'A support referral starts open.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        -- Missing rows are left to the foreign keys, which report them with their own names.
                        SELECT status INTO session_status FROM assessment_sessions WHERE id = NEW.assessment_session_id;
                        IF FOUND AND session_status <> 'Scored' THEN
                            RAISE EXCEPTION 'A support referral follows only from a scored session.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        SELECT kind, status INTO rules_kind, rules_status FROM rule_sets WHERE id = NEW.referral_rule_set_id;
                        IF FOUND AND (rules_kind <> 'Referral' OR rules_status <> 'Approved') THEN
                            RAISE EXCEPTION 'A support referral needs an approved referral rule set.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        -- FOR SHARE, not the foreign key's FOR KEY SHARE: a withdrawal must wait until this transaction ends.
                        SELECT consent_type, withdrawn_at INTO consent_kind, consent_withdrawn_at
                            FROM consent_records WHERE id = NEW.consent_record_id FOR SHARE;
                        IF FOUND AND (consent_kind <> 'Referral' OR consent_withdrawn_at IS NOT NULL) THEN
                            RAISE EXCEPTION 'A support referral needs active referral consent.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF (NEW.id, NEW.assessment_session_id, NEW.family_member_id, NEW.instrument_version_id, NEW.category_code,
                        NEW.referral_rule_set_id, NEW.consent_record_id, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.assessment_session_id, OLD.family_member_id, OLD.instrument_version_id, OLD.category_code,
                        OLD.referral_rule_set_id, OLD.consent_record_id, OLD.created_at)
                    THEN
                        RAISE EXCEPTION 'A support referral keeps its session, member, version, category, rules, consent and creation time.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    -- A service is recorded only by assigning an open referral, and never changes afterwards.
                    IF NEW.assigned_service_code IS DISTINCT FROM OLD.assigned_service_code
                       AND NOT (OLD.status = 'Open' AND NEW.status = 'InProgress')
                    THEN
                        RAISE EXCEPTION 'A service is recorded only when an open support referral is assigned.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    -- Assigning hands the referral on, so the consent must still be active, under the same share lock.
                    IF OLD.status = 'Open' AND NEW.status = 'InProgress' THEN
                        SELECT consent_type, withdrawn_at INTO consent_kind, consent_withdrawn_at
                            FROM consent_records WHERE id = NEW.consent_record_id FOR SHARE;
                        IF NOT FOUND OR consent_kind <> 'Referral' OR consent_withdrawn_at IS NOT NULL THEN
                            RAISE EXCEPTION 'A support referral can be assigned only while its referral consent is active.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;
                    END IF;

                    -- Referrals only move forward; nothing leaves Resolved or Cancelled.
                    IF (OLD.status = 'Open' AND NEW.status IN ('Open', 'InProgress', 'Resolved', 'Cancelled'))
                       OR (OLD.status = 'InProgress' AND NEW.status IN ('InProgress', 'Resolved', 'Cancelled'))
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'This support referral change is not allowed.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_support_referrals_guarded
                    BEFORE INSERT OR UPDATE OR DELETE ON support_referrals
                    FOR EACH ROW EXECUTE FUNCTION azwah_support_referrals_guarded();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_support_referrals_guarded ON support_referrals;
                DROP FUNCTION IF EXISTS azwah_support_referrals_guarded();
                """);
        }
    }
}
