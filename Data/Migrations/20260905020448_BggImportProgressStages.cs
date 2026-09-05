using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class BggImportProgressStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FoundExpansions",
                table: "ClubBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FoundGames",
                table: "ClubBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "ClubBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FoundExpansions",
                table: "CampBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FoundGames",
                table: "CampBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "CampBggImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FoundExpansions",
                table: "ClubBggImports");

            migrationBuilder.DropColumn(
                name: "FoundGames",
                table: "ClubBggImports");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "ClubBggImports");

            migrationBuilder.DropColumn(
                name: "FoundExpansions",
                table: "CampBggImports");

            migrationBuilder.DropColumn(
                name: "FoundGames",
                table: "CampBggImports");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "CampBggImports");
        }
    }
}
