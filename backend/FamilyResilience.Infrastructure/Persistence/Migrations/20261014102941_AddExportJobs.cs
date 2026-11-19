using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dataset = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    columns = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    wave_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instrument_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_jobs", x => x.id);
                    table.CheckConstraint("ck_export_jobs_attempts", "attempts BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_export_jobs_dataset", "dataset IN ('AssessmentSessions', 'ScoreResults')");
                    table.CheckConstraint("ck_export_jobs_row_count", "row_count IS NULL OR row_count >= 0");
                    table.CheckConstraint("ck_export_jobs_status", "status IN ('Queued', 'Running', 'Completed', 'Failed', 'Expired')");
                    table.CheckConstraint("ck_export_jobs_status_shape", "(status = 'Queued' AND attempts = 0 AND started_at IS NULL AND finished_at IS NULL AND row_count IS NULL AND error_code IS NULL AND expires_at IS NULL) OR (status = 'Running' AND attempts >= 1 AND started_at IS NOT NULL AND finished_at IS NULL AND row_count IS NULL AND error_code IS NULL AND expires_at IS NULL) OR (status IN ('Completed', 'Expired') AND attempts >= 1 AND finished_at IS NOT NULL AND row_count IS NOT NULL AND expires_at IS NOT NULL AND error_code IS NULL) OR (status = 'Failed' AND finished_at IS NOT NULL AND error_code IS NOT NULL AND row_count IS NULL AND expires_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_export_jobs_waves_wave_id",
                        column: x => x.wave_id,
                        principalTable: "waves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_completed_expires_at",
                table: "export_jobs",
                column: "expires_at",
                filter: "status = 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_requested_by",
                table: "export_jobs",
                columns: new[] { "requested_by", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_wave",
                table: "export_jobs",
                column: "wave_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_jobs");
        }
    }
}
