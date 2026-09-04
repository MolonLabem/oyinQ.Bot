using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class BgStatsPlayResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HigherScoreWins",
                table: "GatheringPlayRecords",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "GatheringPlayRecords",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsWinner",
                table: "GatheringPlayPlayers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                table: "GatheringPlayPlayers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HigherScoreWins",
                table: "GatheringPlayRecords");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "GatheringPlayRecords");

            migrationBuilder.DropColumn(
                name: "IsWinner",
                table: "GatheringPlayPlayers");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "GatheringPlayPlayers");
        }
    }
}
