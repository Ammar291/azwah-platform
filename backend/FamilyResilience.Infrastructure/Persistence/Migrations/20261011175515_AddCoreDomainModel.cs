using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreDomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consent_policy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_reference_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    document_reference_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_policy_versions", x => x.id);
                    table.UniqueConstraint("ak_consent_policy_versions_id_type", x => new { x.id, x.consent_type });
                    table.CheckConstraint("ck_consent_policy_versions_approval", "status = 'Draft' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL)");
                    table.CheckConstraint("ck_consent_policy_versions_consent_type", "consent_type IN ('Participation', 'ResearchUse', 'Referral')");
                    table.CheckConstraint("ck_consent_policy_versions_retired", "status <> 'Retired' OR retired_at IS NOT NULL");
                    table.CheckConstraint("ck_consent_policy_versions_status", "status IN ('Draft', 'Approved', 'Retired')");
                    table.CheckConstraint("ck_consent_policy_versions_version_number", "version_number > 0");
                });

            migrationBuilder.CreateTable(
                name: "households",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_households", x => x.id);
                    table.CheckConstraint("ck_households_status", "status IN ('Active', 'Withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description_ar = table.Column<string>(type: "text", nullable: false),
                    description_en = table.Column<string>(type: "text", nullable: false),
                    name_ar = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.CheckConstraint("ck_instruments_code", "code ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_instruments_kind", "kind IN ('HouseholdAssessment', 'FamilyVoices')");
                    table.CheckConstraint("ck_instruments_status", "status IN ('Active', 'Retired')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_retry_count", "retry_count >= 0");
                    table.CheckConstraint("ck_outbox_messages_status", "status IN ('Pending', 'Processed', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "waves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waves", x => x.id);
                    table.CheckConstraint("ck_waves_status", "status IN ('Planned', 'Open', 'Closed')");
                    table.CheckConstraint("ck_waves_window", "closes_at > opens_at");
                });

            migrationBuilder.CreateTable(
                name: "family_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_to_household = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    employment_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_head_of_household = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary_income_earner = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    birth_month = table.Column<short>(type: "smallint", nullable: false),
                    birth_year = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_members", x => x.id);
                    table.UniqueConstraint("ak_family_members_id_household", x => new { x.id, x.household_id });
                    table.CheckConstraint("ck_family_members_birth_month", "birth_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_family_members_birth_year", "birth_year >= 1900");
                    table.CheckConstraint("ck_family_members_employment_status", "employment_status IN ('Employed', 'SelfEmployed', 'Unemployed', 'Student', 'Homemaker', 'Retired', 'Other')");
                    table.CheckConstraint("ck_family_members_head_has_no_relationship", "NOT is_head_of_household OR relationship_to_household IS NULL");
                    table.CheckConstraint("ck_family_members_relationship_to_household", "relationship_to_household IN ('Spouse', 'Child', 'Parent', 'Sibling', 'Grandchild', 'Grandparent', 'OtherRelative', 'NonRelative')");
                    table.CheckConstraint("ck_family_members_status", "status IN ('Active', 'Inactive')");
                    table.ForeignKey(
                        name: "fk_family_members_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content_reference_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    content_reference_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_versions", x => x.id);
                    table.CheckConstraint("ck_instrument_versions_approval", "status = 'Draft' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL AND effective_from IS NOT NULL AND content_reference_ar IS NOT NULL AND content_reference_en IS NOT NULL)");
                    table.CheckConstraint("ck_instrument_versions_effective_window", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_instrument_versions_retired", "status <> 'Retired' OR (retired_at IS NOT NULL AND effective_to IS NOT NULL)");
                    table.CheckConstraint("ck_instrument_versions_status", "status IN ('Draft', 'Approved', 'Retired')");
                    table.CheckConstraint("ck_instrument_versions_version_number", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_instrument_versions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    consent_policy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_records", x => x.id);
                    table.UniqueConstraint("ak_consent_records_id_member", x => new { x.id, x.family_member_id });
                    table.CheckConstraint("ck_consent_records_capture_method", "capture_method IN ('InApp', 'PaperForm', 'AssistedByStaff')");
                    table.CheckConstraint("ck_consent_records_consent_type", "consent_type IN ('Participation', 'ResearchUse', 'Referral')");
                    table.CheckConstraint("ck_consent_records_withdrawn_after_granted", "withdrawn_at IS NULL OR withdrawn_at >= granted_at");
                    table.ForeignKey(
                        name: "fk_consent_records_consent_policy_versions_consent_policy_vers",
                        columns: x => new { x.consent_policy_version_id, x.consent_type },
                        principalTable: "consent_policy_versions",
                        principalColumns: new[] { "id", "consent_type" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consent_records_family_member_family_member_id_household_id",
                        columns: x => new { x.family_member_id, x.household_id },
                        principalTable: "family_members",
                        principalColumns: new[] { "id", "household_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_order = table.Column<short>(type: "smallint", nullable: false),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_sections", x => x.id);
                    table.UniqueConstraint("ak_instrument_sections_id_version", x => new { x.id, x.instrument_version_id });
                    table.CheckConstraint("ck_instrument_sections_display_order", "display_order > 0");
                    table.ForeignKey(
                        name: "fk_instrument_sections_instrument_versions_instrument_version_",
                        column: x => x.instrument_version_id,
                        principalTable: "instrument_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_sets", x => x.id);
                    table.UniqueConstraint("ak_rule_sets_id_version", x => new { x.id, x.instrument_version_id });
                    table.CheckConstraint("ck_rule_sets_approval", "status = 'Proposed' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL)");
                    table.CheckConstraint("ck_rule_sets_definition_is_object", "jsonb_typeof(definition) = 'object'");
                    table.CheckConstraint("ck_rule_sets_kind", "kind IN ('Scoring', 'Eligibility', 'Referral')");
                    table.CheckConstraint("ck_rule_sets_retired", "status <> 'Retired' OR retired_at IS NOT NULL");
                    table.CheckConstraint("ck_rule_sets_status", "status IN ('Proposed', 'Approved', 'Retired')");
                    table.CheckConstraint("ck_rule_sets_version_number", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_rule_sets_instrument_versions_instrument_version_id",
                        column: x => x.instrument_version_id,
                        principalTable: "instrument_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_order = table.Column<short>(type: "smallint", nullable: false),
                    item_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    scoring_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    scoring_direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    prompt_ar = table.Column<string>(type: "text", nullable: false),
                    prompt_en = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_items", x => x.id);
                    table.UniqueConstraint("ak_instrument_items_id_version", x => new { x.id, x.instrument_version_id });
                    table.CheckConstraint("ck_instrument_items_display_order", "display_order > 0");
                    table.CheckConstraint("ck_instrument_items_item_type", "item_type IN ('SingleChoice')");
                    table.CheckConstraint("ck_instrument_items_scoring_direction", "scoring_direction IN ('Forward', 'Reverse', 'NotScored')");
                    table.ForeignKey(
                        name: "fk_instrument_items_instrument_sections_instrument_section_id_",
                        columns: x => new { x.instrument_section_id, x.instrument_version_id },
                        principalTable: "instrument_sections",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assessment_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    eligibility_rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_assignments", x => x.id);
                    table.UniqueConstraint("ak_assessment_assignments_id_member_version", x => new { x.id, x.family_member_id, x.instrument_version_id });
                    table.CheckConstraint("ck_assessment_assignments_completed", "status <> 'Completed' OR completed_at IS NOT NULL");
                    table.CheckConstraint("ck_assessment_assignments_expiry", "expires_at IS NULL OR expires_at > assigned_at");
                    table.CheckConstraint("ck_assessment_assignments_reason", "reason IN ('EligibilityRule', 'Reissue')");
                    table.CheckConstraint("ck_assessment_assignments_status", "status IN ('Assigned', 'InProgress', 'Completed', 'Expired', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_assessment_assignments_family_member_family_member_id",
                        column: x => x.family_member_id,
                        principalTable: "family_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_assignments_instrument_section_instrument_sectio",
                        columns: x => new { x.instrument_section_id, x.instrument_version_id },
                        principalTable: "instrument_sections",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_assignments_instrument_versions_instrument_versi",
                        column: x => x.instrument_version_id,
                        principalTable: "instrument_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_assignments_rule_sets_eligibility_rule_set_id_in",
                        columns: x => new { x.eligibility_rule_set_id, x.instrument_version_id },
                        principalTable: "rule_sets",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_assignments_waves_wave_id",
                        column: x => x.wave_id,
                        principalTable: "waves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "response_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_order = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<short>(type: "smallint", nullable: false),
                    label_ar = table.Column<string>(type: "text", nullable: false),
                    label_en = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_response_options", x => x.id);
                    table.UniqueConstraint("ak_response_options_id_item_value", x => new { x.id, x.instrument_item_id, x.value });
                    table.CheckConstraint("ck_response_options_display_order", "display_order > 0");
                    table.ForeignKey(
                        name: "fk_response_options_instrument_items_instrument_item_id",
                        column: x => x.instrument_item_id,
                        principalTable: "instrument_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assessment_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_sessions", x => x.id);
                    table.UniqueConstraint("ak_assessment_sessions_id_member", x => new { x.id, x.family_member_id });
                    table.UniqueConstraint("ak_assessment_sessions_id_version", x => new { x.id, x.instrument_version_id });
                    table.CheckConstraint("ck_assessment_sessions_scored_after_submitted", "scored_at IS NULL OR scored_at >= submitted_at");
                    table.CheckConstraint("ck_assessment_sessions_status", "status IN ('InProgress', 'Submitted', 'Scored', 'Abandoned')");
                    table.CheckConstraint("ck_assessment_sessions_status_shape", "(status = 'InProgress' AND submitted_at IS NULL AND scored_at IS NULL AND abandoned_at IS NULL) OR (status = 'Submitted' AND submitted_at IS NOT NULL AND scored_at IS NULL AND abandoned_at IS NULL) OR (status = 'Scored' AND submitted_at IS NOT NULL AND scored_at IS NOT NULL AND abandoned_at IS NULL) OR (status = 'Abandoned' AND abandoned_at IS NOT NULL AND submitted_at IS NULL)");
                    table.CheckConstraint("ck_assessment_sessions_submitted_after_started", "submitted_at IS NULL OR submitted_at >= started_at");
                    table.ForeignKey(
                        name: "fk_assessment_sessions_assessment_assignments_assessment_assig",
                        columns: x => new { x.assessment_assignment_id, x.family_member_id, x.instrument_version_id },
                        principalTable: "assessment_assignments",
                        principalColumns: new[] { "id", "family_member_id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_sessions_instrument_section_instrument_section_i",
                        columns: x => new { x.instrument_section_id, x.instrument_version_id },
                        principalTable: "instrument_sections",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_value = table.Column<short>(type: "smallint", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_responses", x => x.id);
                    table.CheckConstraint("ck_assessment_responses_modified_after_answered", "last_modified_at >= answered_at");
                    table.ForeignKey(
                        name: "fk_assessment_responses_assessment_sessions_assessment_session",
                        columns: x => new { x.assessment_session_id, x.instrument_version_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_responses_instrument_item_instrument_item_id_ins",
                        columns: x => new { x.instrument_item_id, x.instrument_version_id },
                        principalTable: "instrument_items",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_responses_response_option_response_option_id_ins",
                        columns: x => new { x.response_option_id, x.instrument_item_id, x.raw_value },
                        principalTable: "response_options",
                        principalColumns: new[] { "id", "instrument_item_id", "value" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_events_actor_type",
                table: "audit_events",
                sql: "actor_type IN ('User', 'System', 'Anonymous')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_events_outcome",
                table: "audit_events",
                sql: "outcome IN ('Succeeded', 'Denied', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_assignments_eligibility_rule_set_id_instrument_v",
                table: "assessment_assignments",
                columns: new[] { "eligibility_rule_set_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_assignments_instrument_section_id_instrument_ver",
                table: "assessment_assignments",
                columns: new[] { "instrument_section_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_assignments_instrument_version_id",
                table: "assessment_assignments",
                column: "instrument_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_assignments_wave_status",
                table: "assessment_assignments",
                columns: new[] { "wave_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_assessment_assignments_live",
                table: "assessment_assignments",
                columns: new[] { "family_member_id", "instrument_version_id", "instrument_section_id", "wave_id" },
                unique: true,
                filter: "status NOT IN ('Cancelled', 'Expired')")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_assessment_responses_assessment_session_id_instrument_versi",
                table: "assessment_responses",
                columns: new[] { "assessment_session_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_responses_instrument_item_id_instrument_version_",
                table: "assessment_responses",
                columns: new[] { "instrument_item_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_responses_response_option_id_instrument_item_id_",
                table: "assessment_responses",
                columns: new[] { "response_option_id", "instrument_item_id", "raw_value" });

            migrationBuilder.CreateIndex(
                name: "ux_assessment_responses_session_item",
                table: "assessment_responses",
                columns: new[] { "assessment_session_id", "instrument_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assessment_sessions_assessment_assignment_id_family_member_",
                table: "assessment_sessions",
                columns: new[] { "assessment_assignment_id", "family_member_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_sessions_family_member",
                table: "assessment_sessions",
                column: "family_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_sessions_instrument_section_id_instrument_versio",
                table: "assessment_sessions",
                columns: new[] { "instrument_section_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_assessment_sessions_live",
                table: "assessment_sessions",
                column: "assessment_assignment_id",
                unique: true,
                filter: "status <> 'Abandoned'");

            migrationBuilder.CreateIndex(
                name: "ux_consent_policy_versions_type_version",
                table: "consent_policy_versions",
                columns: new[] { "consent_type", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_consent_policy_version_id_consent_type",
                table: "consent_records",
                columns: new[] { "consent_policy_version_id", "consent_type" });

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_family_member_id_household_id",
                table: "consent_records",
                columns: new[] { "family_member_id", "household_id" });

            migrationBuilder.CreateIndex(
                name: "ux_consent_records_one_active_per_type",
                table: "consent_records",
                columns: new[] { "family_member_id", "consent_type" },
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_household_id",
                table: "family_members",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ux_family_members_one_active_head",
                table: "family_members",
                column: "household_id",
                unique: true,
                filter: "is_head_of_household AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_family_members_one_active_primary_earner",
                table: "family_members",
                column: "household_id",
                unique: true,
                filter: "is_primary_income_earner AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_households_reference",
                table: "households",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instrument_items_instrument_section_id_instrument_version_id",
                table: "instrument_items",
                columns: new[] { "instrument_section_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_instrument_items_section_order",
                table: "instrument_items",
                columns: new[] { "instrument_section_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instrument_items_version_code",
                table: "instrument_items",
                columns: new[] { "instrument_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instrument_sections_version_code",
                table: "instrument_sections",
                columns: new[] { "instrument_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instrument_sections_version_order",
                table: "instrument_sections",
                columns: new[] { "instrument_version_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instrument_versions_instrument_version",
                table: "instrument_versions",
                columns: new[] { "instrument_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instruments_code",
                table: "instruments",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_response_options_item_code",
                table: "response_options",
                columns: new[] { "instrument_item_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_response_options_item_order",
                table: "response_options",
                columns: new[] { "instrument_item_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_response_options_item_value",
                table: "response_options",
                columns: new[] { "instrument_item_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rule_sets_instrument_version_id",
                table: "rule_sets",
                column: "instrument_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_rule_sets_kind_code_version",
                table: "rule_sets",
                columns: new[] { "kind", "code", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_waves_code",
                table: "waves",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_responses");

            migrationBuilder.DropTable(
                name: "consent_records");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "assessment_sessions");

            migrationBuilder.DropTable(
                name: "response_options");

            migrationBuilder.DropTable(
                name: "consent_policy_versions");

            migrationBuilder.DropTable(
                name: "assessment_assignments");

            migrationBuilder.DropTable(
                name: "instrument_items");

            migrationBuilder.DropTable(
                name: "family_members");

            migrationBuilder.DropTable(
                name: "rule_sets");

            migrationBuilder.DropTable(
                name: "waves");

            migrationBuilder.DropTable(
                name: "instrument_sections");

            migrationBuilder.DropTable(
                name: "households");

            migrationBuilder.DropTable(
                name: "instrument_versions");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_events_actor_type",
                table: "audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_events_outcome",
                table: "audit_events");
        }
    }
}
