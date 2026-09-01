using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChatScopedAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatAdminPermissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommunityKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TelegramUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GrantedByTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAdminPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatAdminPermissions_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAdminPermissions_CommunityKey_RevokedAt",
                table: "ChatAdminPermissions",
                columns: new[] { "CommunityKey", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAdminPermissions_TelegramUserId_CommunityKey",
                table: "ChatAdminPermissions",
                columns: new[] { "TelegramUserId", "CommunityKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatAdminPermissions");
        }
    }
}
