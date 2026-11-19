using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "roster_confirmed_at",
                table: "households",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "household_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_household_memberships", x => x.id);
                    table.CheckConstraint("ck_household_memberships_ended_at", "(status = 'Ended') = (ended_at IS NOT NULL) AND (ended_at IS NULL OR ended_at >= created_at)");
                    table.CheckConstraint("ck_household_memberships_role", "role IN ('Manager', 'Member')");
                    table.CheckConstraint("ck_household_memberships_status", "status IN ('Active', 'Ended')");
                    table.ForeignKey(
                        name: "fk_household_memberships_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_household_memberships_member_household",
                        columns: x => new { x.family_member_id, x.household_id },
                        principalTable: "family_members",
                        principalColumns: new[] { "id", "household_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_household_memberships_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_household_memberships_household_id",
                table: "household_memberships",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_household_memberships_member_household",
                table: "household_memberships",
                columns: new[] { "family_member_id", "household_id" });

            migrationBuilder.CreateIndex(
                name: "ix_household_memberships_platform_user",
                table: "household_memberships",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_household_memberships_one_active_per_member",
                table: "household_memberships",
                column: "family_member_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_household_memberships_one_active_per_user",
                table: "household_memberships",
                column: "platform_user_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_platform_users_subject",
                table: "platform_users",
                column: "subject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "household_memberships");

            migrationBuilder.DropTable(
                name: "platform_users");

            migrationBuilder.DropColumn(
                name: "roster_confirmed_at",
                table: "households");
        }
    }
}
