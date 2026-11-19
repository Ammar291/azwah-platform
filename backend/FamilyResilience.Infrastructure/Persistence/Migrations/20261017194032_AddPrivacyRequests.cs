using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivacyRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "privacy_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolution = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_privacy_requests", x => x.id);
                    table.CheckConstraint("ck_privacy_requests_closing", "(status = 'Open' AND closed_at IS NULL AND closed_by IS NULL AND resolution IS NULL) OR (status = 'Closed' AND closed_at >= created_at AND closed_by IS NOT NULL AND resolution IS NOT NULL)");
                    table.CheckConstraint("ck_privacy_requests_resolution", "resolution IN ('Completed', 'Declined', 'Withdrawn')");
                    table.CheckConstraint("ck_privacy_requests_status", "status IN ('Open', 'Closed')");
                    table.CheckConstraint("ck_privacy_requests_type", "type IN ('Access', 'Correction', 'Erasure')");
                    table.ForeignKey(
                        name: "fk_privacy_requests_family_members",
                        columns: x => new { x.family_member_id, x.household_id },
                        principalTable: "family_members",
                        principalColumns: new[] { "id", "household_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_privacy_requests_member_household",
                table: "privacy_requests",
                columns: new[] { "family_member_id", "household_id" });

            migrationBuilder.CreateIndex(
                name: "ix_privacy_requests_status_created",
                table: "privacy_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_privacy_requests_one_open_per_type",
                table: "privacy_requests",
                columns: new[] { "family_member_id", "type" },
                unique: true,
                filter: "status = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "privacy_requests");
        }
    }
}
