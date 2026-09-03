using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGatheringGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameGatheringGuests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameGatheringId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedByParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGatheringGuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameGatheringGuests_GameGatherings_GameGatheringId",
                        column: x => x.GameGatheringId,
                        principalTable: "GameGatherings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGatheringGuests_Participants_CreatedByParticipantId",
                        column: x => x.CreatedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringGuests_CreatedByParticipantId",
                table: "GameGatheringGuests",
                column: "CreatedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringGuests_GameGatheringId_Id",
                table: "GameGatheringGuests",
                columns: new[] { "GameGatheringId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameGatheringGuests");
        }
    }
}
