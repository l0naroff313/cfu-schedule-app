using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversitySchedule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMutationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_data_mutation_receipts",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mutation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_kind = table.Column<int>(type: "integer", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_data_mutation_receipts", x => new { x.installation_id, x.mutation_id });
                    table.ForeignKey(
                        name: "FK_personal_data_mutation_receipts_installations_installation_~",
                        column: x => x.installation_id,
                        principalTable: "installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mutation_receipts_processed_at_utc",
                table: "personal_data_mutation_receipts",
                column: "processed_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_data_mutation_receipts");
        }
    }
}
