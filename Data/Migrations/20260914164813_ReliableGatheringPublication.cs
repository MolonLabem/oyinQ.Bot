using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReliableGatheringPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationOperationId",
                table: "GameGatherings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreationRequestHash",
                table: "GameGatherings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicationAttemptId",
                table: "GameGatherings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublicationLeaseExpiresAt",
                table: "GameGatherings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PublicationRevision",
                table: "GameGatherings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Old send failures did not distinguish a rejected send from a lost receipt.
            migrationBuilder.Sql("""
                UPDATE "GameGatherings" SET "PublicationStatus" = 5,
                    "PublicationError" = 'Исход прежней отправки неизвестен. Проверьте объявление в группе; повторная отправка отключена.'
                WHERE "TelegramMessageId" IS NULL AND "PublicationAttempts" > 0 AND "PublicationStatus" IN (0, 2);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_CommunityKey_OrganizerParticipantId_Creation~",
                table: "GameGatherings",
                columns: new[] { "CommunityKey", "OrganizerParticipantId", "CreationOperationId" },
                unique: true,
                filter: "\"CreationOperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameGatherings_CommunityKey_OrganizerParticipantId_Creation~",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "CreationOperationId",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "CreationRequestHash",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationAttemptId",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationLeaseExpiresAt",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationRevision",
                table: "GameGatherings");
        }
    }
}
