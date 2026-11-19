using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_referrals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    referral_rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_service_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resolution_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_referrals", x => x.id);
                    table.CheckConstraint("ck_support_referrals_status", "status IN ('Open', 'InProgress', 'Resolved', 'Cancelled')");
                    table.CheckConstraint("ck_support_referrals_status_shape", "(status = 'Open' AND assigned_service_code IS NULL AND resolution_code IS NULL AND resolved_at IS NULL AND resolved_by IS NULL) OR (status = 'InProgress' AND assigned_service_code IS NOT NULL AND resolution_code IS NULL AND resolved_at IS NULL AND resolved_by IS NULL) OR (status IN ('Resolved', 'Cancelled') AND resolution_code IS NOT NULL AND resolved_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_support_referrals_consent_member",
                        columns: x => new { x.consent_record_id, x.family_member_id },
                        principalTable: "consent_records",
                        principalColumns: new[] { "id", "family_member_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_referrals_rule_set_version",
                        columns: x => new { x.referral_rule_set_id, x.instrument_version_id },
                        principalTable: "rule_sets",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_referrals_session_member",
                        columns: x => new { x.assessment_session_id, x.family_member_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "family_member_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_referrals_session_version",
                        columns: x => new { x.assessment_session_id, x.instrument_version_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_consent_member",
                table: "support_referrals",
                columns: new[] { "consent_record_id", "family_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_rule_set_version",
                table: "support_referrals",
                columns: new[] { "referral_rule_set_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_session_member",
                table: "support_referrals",
                columns: new[] { "assessment_session_id", "family_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_session_version",
                table: "support_referrals",
                columns: new[] { "assessment_session_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_status_created",
                table: "support_referrals",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_support_referrals_live",
                table: "support_referrals",
                columns: new[] { "assessment_session_id", "category_code" },
                unique: true,
                filter: "status <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_referrals");
        }
    }
}
