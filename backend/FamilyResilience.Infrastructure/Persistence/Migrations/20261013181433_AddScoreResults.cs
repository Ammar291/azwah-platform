using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "score_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scoring_policy_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scoring_algorithm_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    incomplete_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    total_score = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    tier_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_results", x => x.id);
                    table.UniqueConstraint("ak_score_results_id_version", x => new { x.id, x.instrument_version_id });
                    table.CheckConstraint("ck_score_results_algorithm_version", "scoring_algorithm_version > 0");
                    table.CheckConstraint("ck_score_results_status", "status IN ('Complete', 'Incomplete')");
                    table.CheckConstraint("ck_score_results_status_shape", "(status = 'Complete' AND incomplete_reason IS NULL) OR (status = 'Incomplete' AND incomplete_reason IS NOT NULL AND total_score IS NULL AND tier_code IS NULL)");
                    table.CheckConstraint("ck_score_results_tier_needs_total", "tier_code IS NULL OR total_score IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_score_results_assessment_sessions_assessment_session_id_ins",
                        columns: x => new { x.assessment_session_id, x.instrument_version_id },
                        principalTable: "assessment_sessions",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_score_results_rule_sets_rule_set_id_instrument_version_id",
                        columns: x => new { x.rule_set_id, x.instrument_version_id },
                        principalTable: "rule_sets",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "score_category_scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    score_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    score = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_category_scores", x => x.id);
                    table.CheckConstraint("ck_score_category_scores_item_count", "item_count > 0");
                    table.ForeignKey(
                        name: "fk_score_category_scores_score_results_score_result_id",
                        column: x => x.score_result_id,
                        principalTable: "score_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "score_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    score_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_value = table.Column<short>(type: "smallint", nullable: false),
                    calculated_value = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    is_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    scoring_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_contributions", x => x.id);
                    table.CheckConstraint("ck_score_contributions_excluded", "(is_excluded AND calculated_value IS NULL) OR (NOT is_excluded AND calculated_value IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_score_contributions_instrument_items_instrument_item_id_ins",
                        columns: x => new { x.instrument_item_id, x.instrument_version_id },
                        principalTable: "instrument_items",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_score_contributions_score_results_score_result_id_instrumen",
                        columns: x => new { x.score_result_id, x.instrument_version_id },
                        principalTable: "score_results",
                        principalColumns: new[] { "id", "instrument_version_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_score_category_scores_result_category",
                table: "score_category_scores",
                columns: new[] { "score_result_id", "category_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_score_contributions_instrument_item_id_instrument_version_id",
                table: "score_contributions",
                columns: new[] { "instrument_item_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_score_contributions_score_result_id_instrument_version_id",
                table: "score_contributions",
                columns: new[] { "score_result_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_score_contributions_result_item",
                table: "score_contributions",
                columns: new[] { "score_result_id", "instrument_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_score_results_assessment_session_id_instrument_version_id",
                table: "score_results",
                columns: new[] { "assessment_session_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_score_results_rule_set_id_instrument_version_id",
                table: "score_results",
                columns: new[] { "rule_set_id", "instrument_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_score_results_session_rule_set",
                table: "score_results",
                columns: new[] { "assessment_session_id", "rule_set_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "score_category_scores");

            migrationBuilder.DropTable(
                name: "score_contributions");

            migrationBuilder.DropTable(
                name: "score_results");
        }
    }
}
