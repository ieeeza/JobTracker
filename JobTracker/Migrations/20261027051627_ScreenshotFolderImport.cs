using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class ScreenshotFolderImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchImageHashes",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BatchImportKey",
                table: "Jobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScreenshotImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Folder = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenshotImportBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_BatchImportKey",
                table: "Jobs",
                column: "BatchImportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreenshotImportBatches_Fingerprint",
                table: "ScreenshotImportBatches",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreenshotImportBatches_StartedAtUtc",
                table: "ScreenshotImportBatches",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreenshotImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_BatchImportKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "BatchImageHashes",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "BatchImportKey",
                table: "Jobs");
        }
    }
}
