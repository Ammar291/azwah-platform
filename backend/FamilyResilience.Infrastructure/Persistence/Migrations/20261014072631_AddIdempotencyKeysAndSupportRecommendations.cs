using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeysAndSupportRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.id);
                    table.CheckConstraint("ck_idempotency_keys_expiry", "expires_at > created_at");
                });

            migrationBuilder.CreateTable(
                name: "support_recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referral_rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    support_referral_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "fk_support_recommendations_referral",
                        column: x => x.support_referral_id,
                        principalTable: "support_referrals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_recommendations_rule_set_version",
                        columns: x => new { x.referral_rule_set_id, x.instrument_version_id },
                        principalTable: "rule_sets",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_recommendations_score_result",
                        column: x => x.score_result_id,
                        principalTable: "score_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_recommendations_session_member",
                        columns: x => new { x.assessment_session_id, x.family_member_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "family_member_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_recommendations_session_version",
                        columns: x => new { x.assessment_session_id, x.instrument_version_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_keys_subject_key",
                table: "idempotency_keys",
                columns: new[] { "subject", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_recommendations_member_created",
                table: "support_recommendations",
                columns: new[] { "family_member_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_recommendations_referral",
                table: "support_recommendations",
                column: "support_referral_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_recommendations_rule_set_version",
                table: "support_recommendations",
                columns: new[] { "referral_rule_set_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_recommendations_session_member",
                table: "support_recommendations",
                columns: new[] { "assessment_session_id", "family_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_recommendations_session_version",
                table: "support_recommendations",
                columns: new[] { "assessment_session_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_support_recommendations_score_category",
                table: "support_recommendations",
                columns: new[] { "score_result_id", "category_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "support_recommendations");
        }
    }
}
