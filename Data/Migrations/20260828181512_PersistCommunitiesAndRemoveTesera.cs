using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistCommunitiesAndRemoveTesera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "CollectionImports"
                SET "Status" = 3,
                    "Error" = 'Tesera import is no longer supported. Use BGG.',
                    "CompletedAt" = COALESCE("CompletedAt", NOW()),
                    "UpdatedAt" = NOW()
                WHERE "Provider" = 1 AND "Status" IN (0, 1);
                """);

            migrationBuilder.DropIndex(
                name: "IX_Games_TeseraAlias",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TeseraAlias",
                table: "Games");

            migrationBuilder.CreateTable(
                name: "OyinQCommunities",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OyinQCommunities", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities",
                column: "TelegramChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OyinQCommunities");

            migrationBuilder.AddColumn<string>(
                name: "TeseraAlias",
                table: "Games",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_TeseraAlias",
                table: "Games",
                column: "TeseraAlias",
                unique: true);
        }
    }
}
