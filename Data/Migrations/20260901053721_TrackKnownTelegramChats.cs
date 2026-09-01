using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackKnownTelegramChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnownTelegramChats",
                columns: table => new
                {
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsBotPresent = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownTelegramChats", x => x.TelegramChatId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnownTelegramChats_IsBotPresent",
                table: "KnownTelegramChats",
                column: "IsBotPresent");

            migrationBuilder.Sql("""
                INSERT INTO "KnownTelegramChats"
                    ("TelegramChatId", "Title", "Username", "IsBotPresent", "FirstSeenAt", "UpdatedAt")
                SELECT "TelegramChatId", "Name", NULL, TRUE, "CreatedAt", "UpdatedAt"
                FROM "OyinQCommunities"
                ON CONFLICT ("TelegramChatId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnownTelegramChats");
        }
    }
}
