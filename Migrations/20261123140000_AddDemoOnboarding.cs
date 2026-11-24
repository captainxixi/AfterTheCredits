using Microsoft.EntityFrameworkCore.Migrations;

namespace AfterTheCredits.Migrations;

public partial class AddDemoOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "OnboardingCompleted", table: "DemoVisitors",
            type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(
            name: "SkippedStarterMovieIds", table: "DemoVisitors",
            type: "TEXT", nullable: false, defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnboardingCompleted", table: "DemoVisitors");
        migrationBuilder.DropColumn(name: "SkippedStarterMovieIds", table: "DemoVisitors");
    }
}
