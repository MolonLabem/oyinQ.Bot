using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CampOperatingInstants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Camps_Status_EndDate_Id",
                table: "Camps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Camps_DateRange",
                table: "Camps");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndsAtUtc",
                table: "Camps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartsAtUtc",
                table: "Camps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Camps" AS c SET
                    "StartsAtUtc" = c."StartDate"::timestamp AT TIME ZONE o."TimeZoneId",
                    "EndsAtUtc" = (c."EndDate" + 1)::timestamp AT TIME ZONE o."TimeZoneId"
                FROM "OyinQCommunities" AS o
                WHERE o."Key" = c."BotChatKey" AND c."StartDate" IS NOT NULL AND c."EndDate" IS NOT NULL;
                """);
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Camps");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Camps");

            migrationBuilder.CreateIndex(
                name: "IX_Camps_Status_EndsAtUtc_Id",
                table: "Camps",
                columns: new[] { "Status", "EndsAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Camps_OperatingWindow",
                table: "Camps",
                sql: "(\"StartsAtUtc\" IS NULL AND \"EndsAtUtc\" IS NULL) OR (\"StartsAtUtc\" IS NOT NULL AND \"EndsAtUtc\" IS NOT NULL AND \"StartsAtUtc\" < \"EndsAtUtc\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Миграция только вперёд: точное время кэмпа нельзя откатить к датам без потери данных.");
    }
}
