using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ForumPostingTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PostingMessageThreadId",
                table: "OyinQCommunities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PostingTopicInvalidatedAt",
                table: "OyinQCommunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsForum",
                table: "KnownTelegramChats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TelegramForumTopics",
                columns: table => new
                {
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    MessageThreadId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramForumTopics", x => new { x.TelegramChatId, x.MessageThreadId });
                    table.ForeignKey(
                        name: "FK_TelegramForumTopics_KnownTelegramChats_TelegramChatId",
                        column: x => x.TelegramChatId,
                        principalTable: "KnownTelegramChats",
                        principalColumn: "TelegramChatId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramForumTopics_TelegramChatId_IsDeleted_IsClosed",
                table: "TelegramForumTopics",
                columns: new[] { "TelegramChatId", "IsDeleted", "IsClosed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramForumTopics");

            migrationBuilder.DropColumn(
                name: "PostingMessageThreadId",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "PostingTopicInvalidatedAt",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "IsForum",
                table: "KnownTelegramChats");
        }
    }
}
