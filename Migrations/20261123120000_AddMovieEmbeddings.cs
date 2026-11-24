using Microsoft.EntityFrameworkCore.Migrations;

namespace AfterTheCredits.Migrations;

public partial class AddMovieEmbeddings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MovieEmbeddings",
            columns: table => new
            {
                CacheKey = table.Column<string>(type: "TEXT", nullable: false),
                VectorJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MovieEmbeddings", row => row.CacheKey));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MovieEmbeddings");
    }
}
