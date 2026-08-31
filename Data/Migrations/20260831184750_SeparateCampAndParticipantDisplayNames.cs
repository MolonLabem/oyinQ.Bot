using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCampAndParticipantDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "CampRegistrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CampRegistrations" AS registration
                SET "DisplayName" = LEFT(COALESCE(NULLIF(BTRIM(participant."PreferredDisplayName"), ''),
                    NULLIF(BTRIM(participant."DisplayName"), '')), 128)
                FROM "Participants" AS participant
                WHERE participant."Id" = registration."ParticipantId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "CampRegistrations");
        }
    }
}
