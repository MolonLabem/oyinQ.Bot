using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommunityDeletionAndTelegramPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "OyinQCommunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramPhotoFileId",
                table: "KnownTelegramChats",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TelegramPhotoUpdatedAt",
                table: "KnownTelegramChats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities",
                column: "TelegramChatId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "TelegramPhotoFileId",
                table: "KnownTelegramChats");

            migrationBuilder.DropColumn(
                name: "TelegramPhotoUpdatedAt",
                table: "KnownTelegramChats");

            migrationBuilder.CreateIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities",
                column: "TelegramChatId",
                unique: true);
        }
    }
}
