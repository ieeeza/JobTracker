using Microsoft.EntityFrameworkCore.Migrations;

namespace JobTracker.Migrations;

public partial class LocalSqlitePersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        RebuildJobs(migrationBuilder, requireMetadata: false);

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_AppliedAt", table: "Jobs", column: "AppliedAt");
        migrationBuilder.CreateIndex(
            name: "IX_Jobs_NormalizedUrl", table: "Jobs", column: "NormalizedUrl", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_Jobs_Source_ExternalJobId", table: "Jobs",
            columns: new[] { "Source", "ExternalJobId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RebuildJobs(migrationBuilder, requireMetadata: true);
        migrationBuilder.CreateIndex(
            name: "IX_Jobs_Source_ExternalJobId", table: "Jobs",
            columns: new[] { "Source", "ExternalJobId" });
    }

    private static void RebuildJobs(MigrationBuilder migrationBuilder, bool requireMetadata)
    {
        // Jobs não possui relacionamentos. A cópia explícita evita o PRAGMA fora da transação
        // gerado por AlterColumn no SQLite; esquema, dados e índices mudam atomicamente.
        migrationBuilder.CreateTable(
            name: "__Jobs_LocalSqlitePersistence",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ExternalJobId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                OriginalUrl = table.Column<string>(type: "TEXT", nullable: false),
                NormalizedUrl = table.Column<string>(type: "TEXT", nullable: false),
                Company = table.Column<string>(type: "TEXT", maxLength: 200, nullable: !requireMetadata),
                Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: !requireMetadata),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Jobs", row => row.Id));

        // No downgrade, valores nulos falham antes da substituição da tabela, sem criar dados fictícios.
        migrationBuilder.Sql("""
            INSERT INTO "__Jobs_LocalSqlitePersistence"
                ("Id", "Source", "ExternalJobId", "OriginalUrl", "NormalizedUrl", "Company", "Title", "Description", "AppliedAt")
            SELECT "Id", "Source", "ExternalJobId", "OriginalUrl", "NormalizedUrl", "Company", "Title", "Description", "AppliedAt"
            FROM "Jobs";
            """);

        migrationBuilder.DropTable(name: "Jobs");
        migrationBuilder.RenameTable(name: "__Jobs_LocalSqlitePersistence", newName: "Jobs");
    }
}
