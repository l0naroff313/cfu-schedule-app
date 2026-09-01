using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversitySchedule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndScheduleCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_catalog_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    source_generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    imported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_catalog_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reference_catalog_import_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    program_count = table.Column<int>(type: "integer", nullable: false),
                    group_count = table.Column<int>(type: "integer", nullable: false),
                    teacher_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_catalog_import_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_source_documents",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_source_documents", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reference_catalog_content_hash",
                table: "reference_catalog_documents",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_reference_catalog_import_started_at",
                table: "reference_catalog_import_logs",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_source_documents_fetched_at",
                table: "schedule_source_documents",
                column: "fetched_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_catalog_documents");

            migrationBuilder.DropTable(
                name: "reference_catalog_import_logs");

            migrationBuilder.DropTable(
                name: "schedule_source_documents");
        }
    }
}
