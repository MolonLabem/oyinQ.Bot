using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExactCampAttendanceDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampRegistrationDays",
                columns: table => new
                {
                    CampRegistrationId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampRegistrationDays", x => new { x.CampRegistrationId, x.Date });
                    table.ForeignKey(
                        name: "FK_CampRegistrationDays_CampRegistrations_CampRegistrationId",
                        column: x => x.CampRegistrationId,
                        principalTable: "CampRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampRegistrationDays");
        }
    }
}
