using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CampCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationJson",
                table: "Camps",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"version\":1,\"registrationFields\":[]}");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationDataJson",
                table: "CampRegistrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"version\":1,\"answers\":{}}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigurationJson",
                table: "Camps");

            migrationBuilder.DropColumn(
                name: "RegistrationDataJson",
                table: "CampRegistrations");
        }
    }
}
