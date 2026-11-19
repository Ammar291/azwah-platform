using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    visibility = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    categories = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    audiences = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    service_languages = table.Column<string[]>(type: "character varying(2)[]", nullable: false),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    review_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('english'::regconfig, coalesce(name_en,'')),'A') || setweight(to_tsvector('english'::regconfig, coalesce(summary_en,'')),'B') || setweight(to_tsvector('english'::regconfig, coalesce(provider_name_en,'')),'C') || setweight(to_tsvector('simple'::regconfig, regexp_replace(regexp_replace(translate(coalesce(name_ar, ''), 'أإآٱىة', 'اااايه'), '[\\u064B-\\u065F\\u0670\\u0640]', '', 'g'), '(^|[^ء-ي])(وال|فال|بال|كال|لل|ال)(?=[ء-ي]{2,})', '\\1', 'g')),'A') || setweight(to_tsvector('simple'::regconfig, regexp_replace(regexp_replace(translate(coalesce(summary_ar, ''), 'أإآٱىة', 'اااايه'), '[\\u064B-\\u065F\\u0670\\u0640]', '', 'g'), '(^|[^ء-ي])(وال|فال|بال|كال|لل|ال)(?=[ء-ي]{2,})', '\\1', 'g')),'B') || setweight(to_tsvector('simple'::regconfig, regexp_replace(regexp_replace(translate(coalesce(provider_name_ar, ''), 'أإآٱىة', 'اااايه'), '[\\u064B-\\u065F\\u0670\\u0640]', '', 'g'), '(^|[^ء-ي])(وال|فال|بال|كال|لل|ال)(?=[ء-ي]{2,})', '\\1', 'g')),'C')", stored: true),
                    name_ar = table.Column<string>(type: "text", nullable: true),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    provider_name_ar = table.Column<string>(type: "text", nullable: true),
                    provider_name_en = table.Column<string>(type: "text", nullable: false),
                    summary_ar = table.Column<string>(type: "text", nullable: true),
                    summary_en = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_resources", x => x.id);
                    table.CheckConstraint("ck_support_resources_approval", "status = 'Draft' OR (name_ar IS NOT NULL AND summary_ar IS NOT NULL AND provider_name_ar IS NOT NULL AND approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL AND review_due_at IS NOT NULL AND review_due_at > approved_at)");
                    table.CheckConstraint("ck_support_resources_audiences", "cardinality(audiences) <= 10");
                    table.CheckConstraint("ck_support_resources_categories", "cardinality(categories) BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_support_resources_retired", "(status = 'Retired') = (retired_at IS NOT NULL)");
                    table.CheckConstraint("ck_support_resources_service_languages", "cardinality(service_languages) BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_support_resources_status", "status IN ('Draft', 'Approved', 'Retired')");
                    table.CheckConstraint("ck_support_resources_url_https", "url LIKE 'https://%'");
                    table.CheckConstraint("ck_support_resources_version_number", "version_number >= 1");
                    table.CheckConstraint("ck_support_resources_visibility", "visibility IN ('Everyone', 'StaffOnly')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_resources_discoverable",
                table: "support_resources",
                column: "review_due_at",
                filter: "status = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_support_resources_search_vector",
                table: "support_resources",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ux_support_resources_code_version",
                table: "support_resources",
                columns: new[] { "code", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_support_resources_one_approved_per_code",
                table: "support_resources",
                column: "code",
                unique: true,
                filter: "status = 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_resources");
        }
    }
}
