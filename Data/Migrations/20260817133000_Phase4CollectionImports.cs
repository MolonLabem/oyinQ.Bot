using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using oyinQ.Bot.Data;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260817133000_Phase4CollectionImports")]
public partial class Phase4CollectionImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "RequestedByTelegramUserId",
            table: "CollectionImports",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_CollectionImports_RequestedByTelegramUserId",
            table: "CollectionImports",
            column: "RequestedByTelegramUserId");

        migrationBuilder.CreateIndex(
            name: "IX_GameCopies_GameId_Club",
            table: "GameCopies",
            column: "GameId",
            unique: true,
            filter: "\"OwnerParticipantId\" IS NULL AND \"Source\" = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CollectionImports_RequestedByTelegramUserId",
            table: "CollectionImports");

        migrationBuilder.DropIndex(
            name: "IX_GameCopies_GameId_Club",
            table: "GameCopies");

        migrationBuilder.DropColumn(
            name: "RequestedByTelegramUserId",
            table: "CollectionImports");
    }
}
