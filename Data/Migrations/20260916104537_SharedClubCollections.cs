using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedClubCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceClubId",
                table: "Clubs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_SourceClubId",
                table: "Clubs",
                column: "SourceClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clubs_Clubs_SourceClubId",
                table: "Clubs",
                column: "SourceClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clubs_Clubs_SourceClubId",
                table: "Clubs");

            migrationBuilder.DropIndex(
                name: "IX_Clubs_SourceClubId",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "SourceClubId",
                table: "Clubs");
        }
    }
}
