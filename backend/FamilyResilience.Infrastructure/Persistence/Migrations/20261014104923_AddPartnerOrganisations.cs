using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerOrganisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hand_off_method",
                table: "support_referrals",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handed_off_at",
                table: "support_referrals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handed_off_by",
                table: "support_referrals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "partner_organisation_id",
                table: "support_referrals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "partner_organisations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    categories = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name_ar = table.Column<string>(type: "text", nullable: true),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_organisations", x => x.id);
                    table.CheckConstraint("ck_partner_organisations_categories", "cardinality(categories) BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_partner_organisations_name_ar", "name_ar IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_referrals_partner",
                table: "support_referrals",
                column: "partner_organisation_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_support_referrals_hand_off_method",
                table: "support_referrals",
                sql: "hand_off_method IN ('Manual')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_support_referrals_hand_off_shape",
                table: "support_referrals",
                sql: "(partner_organisation_id IS NULL AND hand_off_method IS NULL AND handed_off_at IS NULL AND handed_off_by IS NULL) OR (partner_organisation_id IS NOT NULL AND hand_off_method IS NOT NULL AND handed_off_at IS NOT NULL AND handed_off_by IS NOT NULL AND assigned_service_code IS NOT NULL AND status <> 'Open')");

            migrationBuilder.CreateIndex(
                name: "ux_partner_organisations_code",
                table: "partner_organisations",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_support_referrals_partner",
                table: "support_referrals",
                column: "partner_organisation_id",
                principalTable: "partner_organisations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_support_referrals_partner",
                table: "support_referrals");

            migrationBuilder.DropTable(
                name: "partner_organisations");

            migrationBuilder.DropIndex(
                name: "ix_support_referrals_partner",
                table: "support_referrals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_support_referrals_hand_off_method",
                table: "support_referrals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_support_referrals_hand_off_shape",
                table: "support_referrals");

            migrationBuilder.DropColumn(
                name: "hand_off_method",
                table: "support_referrals");

            migrationBuilder.DropColumn(
                name: "handed_off_at",
                table: "support_referrals");

            migrationBuilder.DropColumn(
                name: "handed_off_by",
                table: "support_referrals");

            migrationBuilder.DropColumn(
                name: "partner_organisation_id",
                table: "support_referrals");
        }
    }
}
