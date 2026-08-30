using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class GatheringHistoryAndCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramMessageCleanups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramMessageCleanups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessageCleanups_LastAttemptAt_CreatedAt",
                table: "TelegramMessageCleanups",
                columns: new[] { "LastAttemptAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_Status_StartsAtUtc_Id",
                table: "GameGatherings",
                columns: new[] { "Status", "StartsAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessageCleanups_TelegramChatId_TelegramMessageId",
                table: "TelegramMessageCleanups",
                columns: new[] { "TelegramChatId", "TelegramMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameGatherings_Status_StartsAtUtc_Id",
                table: "GameGatherings");

            migrationBuilder.DropTable(
                name: "TelegramMessageCleanups");
        }
    }
}
