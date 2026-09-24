using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CampVisibilityDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampParticipantVisibilities",
                columns: table => new
                {
                    CampId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    ShareCollection = table.Column<bool>(type: "boolean", nullable: false),
                    ShareWishes = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampParticipantVisibilities", x => new { x.CampId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_CampParticipantVisibilities_Camps_CampId",
                        column: x => x.CampId,
                        principalTable: "Camps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampParticipantVisibilities_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampParticipantVisibilities_ParticipantId",
                table: "CampParticipantVisibilities",
                column: "ParticipantId");

            // Old false values cannot be distinguished from an explicit refusal.
            // Preserve every existing choice; only absent settings are open by default.
            migrationBuilder.Sql("""
                INSERT INTO "CampParticipantVisibilities" ("CampId", "ParticipantId", "ShareCollection", "ShareWishes")
                SELECT "CampId", "ParticipantId", "ShareCollection", "ShareWishes"
                FROM "CampRegistrations";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CampRegistrations" AS r
                SET "ShareCollection" = COALESCE((SELECT v."ShareCollection" FROM "CampParticipantVisibilities" AS v
                        WHERE v."CampId" = r."CampId" AND v."ParticipantId" = r."ParticipantId"), TRUE),
                    "ShareWishes" = COALESCE((SELECT v."ShareWishes" FROM "CampParticipantVisibilities" AS v
                        WHERE v."CampId" = r."CampId" AND v."ParticipantId" = r."ParticipantId"), TRUE);
                """);
            migrationBuilder.DropTable(
                name: "CampParticipantVisibilities");
        }
    }
}
