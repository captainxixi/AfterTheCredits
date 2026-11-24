using Microsoft.EntityFrameworkCore.Migrations;

namespace AfterTheCredits.Migrations;

public partial class IsolateDemoVisitors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(name: "WatchlistEntries", newName: "LegacyWatchlistEntries");

        migrationBuilder.CreateTable(
            name: "DemoVisitors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DemoVisitors", row => row.Id));

        migrationBuilder.CreateTable(
            name: "WatchlistEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                VisitorId = table.Column<Guid>(type: "TEXT", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                IsWatched = table.Column<bool>(type: "INTEGER", nullable: false),
                MyRating = table.Column<double>(type: "REAL", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WatchlistEntries", row => row.Id);
                table.ForeignKey("FK_WatchlistEntries_DemoVisitors_VisitorId",
                    row => row.VisitorId, "DemoVisitors", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DemoVisitors_ExpiresAtUtc", table: "DemoVisitors", column: "ExpiresAtUtc");
        migrationBuilder.CreateIndex(
            name: "IX_WatchlistEntries_VisitorId_MovieId", table: "WatchlistEntries",
            columns: new[] { "VisitorId", "MovieId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WatchlistEntries");
        migrationBuilder.DropTable(name: "DemoVisitors");
        migrationBuilder.RenameTable(name: "LegacyWatchlistEntries", newName: "WatchlistEntries");
    }
}
