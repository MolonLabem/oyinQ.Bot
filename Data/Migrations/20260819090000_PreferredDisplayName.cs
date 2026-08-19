using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using oyinQ.Bot.Data;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260819090000_PreferredDisplayName")]
public partial class PreferredDisplayName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreferredDisplayName",
            table: "Participants",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PreferredDisplayName",
            table: "Participants");
    }
}
