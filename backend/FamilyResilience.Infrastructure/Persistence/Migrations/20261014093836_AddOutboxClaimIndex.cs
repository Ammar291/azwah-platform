using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyResilience.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaimIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending_type_occurred_at",
                table: "outbox_messages",
                columns: new[] { "type", "occurred_at" },
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending_type_occurred_at",
                table: "outbox_messages");
        }
    }
}
