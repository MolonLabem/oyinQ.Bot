using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AtomicClubMetadataRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StagedCollectionJson",
                table: "ClubMetadataRefreshes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedGames",
                table: "ClubMetadataRefreshes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StagedCollectionJson",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropColumn(
                name: "UpdatedGames",
                table: "ClubMetadataRefreshes");
        }
    }
}
