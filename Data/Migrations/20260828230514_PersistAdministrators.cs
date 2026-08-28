using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistAdministrators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OyinQAdministrators",
                columns: table => new
                {
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    AddedByTelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OyinQAdministrators", x => x.TelegramUserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OyinQAdministrators");
        }
    }
}
