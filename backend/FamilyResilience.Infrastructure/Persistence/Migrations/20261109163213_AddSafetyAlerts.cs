using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSafetyAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "safety_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    household_id = table.Column<Guid>(type: "uuid", nullable: true),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    concern = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detector = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    signal_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    assessment_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    acknowledged_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_safety_alerts", x => x.id);
                    table.CheckConstraint("ck_safety_alerts_concern", "concern IN ('SuicideOrSelfHarm', 'HarmToOthers', 'AbuseOrDanger', 'LowResilienceScore')");
                    table.CheckConstraint("ck_safety_alerts_detector", "detector IN ('PhraseScreen', 'AiModeration', 'ProviderFilter', 'ScoreBand')");
                    table.CheckConstraint("ck_safety_alerts_language", "language IS NULL OR language IN ('en', 'ar')");
                    table.CheckConstraint("ck_safety_alerts_member_household", "family_member_id IS NULL OR household_id IS NOT NULL");
                    table.CheckConstraint("ck_safety_alerts_occurrences", "occurrence_count >= 1 AND last_detected_at >= first_detected_at");
                    table.CheckConstraint("ck_safety_alerts_resolution", "resolution IN ('FamilyContacted', 'ReferredToService', 'EmergencyServicesContacted', 'NoActionNeeded', 'FalseAlarm', 'UnableToContact')");
                    table.CheckConstraint("ck_safety_alerts_severity", "severity IN ('Critical', 'High')");
                    table.CheckConstraint("ck_safety_alerts_severity_of_concern", "(concern = 'LowResilienceScore') = (severity = 'High')");
                    table.CheckConstraint("ck_safety_alerts_source", "source IN ('AssistantChat', 'ConsultantVoice', 'SupportSearch', 'HouseholdAssessment')");
                    table.CheckConstraint("ck_safety_alerts_status", "status IN ('Open', 'Acknowledged', 'Resolved')");
                    table.CheckConstraint("ck_safety_alerts_status_shape", "(status = 'Open' AND acknowledged_at IS NULL AND acknowledged_by IS NULL AND resolved_at IS NULL AND resolved_by IS NULL AND resolution IS NULL) OR (status = 'Acknowledged' AND acknowledged_at >= first_detected_at AND acknowledged_by IS NOT NULL AND resolved_at IS NULL AND resolved_by IS NULL AND resolution IS NULL) OR (status = 'Resolved' AND resolved_at >= first_detected_at AND resolved_by IS NOT NULL AND resolution IS NOT NULL AND (acknowledged_at IS NULL) = (acknowledged_by IS NULL))");
                    table.CheckConstraint("ck_safety_alerts_subject", "platform_user_id IS NOT NULL OR household_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_safety_alerts_assessment_sessions_assessment_session_id",
                        column: x => x.assessment_session_id,
                        principalTable: "assessment_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_safety_alerts_family_members",
                        columns: x => new { x.family_member_id, x.household_id },
                        principalTable: "family_members",
                        principalColumns: new[] { "id", "household_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_safety_alerts_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_safety_alerts_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_safety_alerts_household",
                table: "safety_alerts",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_safety_alerts_member_household",
                table: "safety_alerts",
                columns: new[] { "family_member_id", "household_id" });

            migrationBuilder.CreateIndex(
                name: "ix_safety_alerts_platform_user",
                table: "safety_alerts",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_safety_alerts_queue",
                table: "safety_alerts",
                columns: new[] { "status", "severity", "last_detected_at" });

            migrationBuilder.CreateIndex(
                name: "ux_safety_alerts_session",
                table: "safety_alerts",
                column: "assessment_session_id",
                unique: true,
                filter: "assessment_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_safety_alerts_unresolved_subject",
                table: "safety_alerts",
                columns: new[] { "platform_user_id", "concern", "source" },
                unique: true,
                filter: "status <> 'Resolved' AND platform_user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "safety_alerts");
        }
    }
}
