using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubBggUsernameImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubBggImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<long>(type: "bigint", nullable: false),
                    BggUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProgressCurrent = table.Column<int>(type: "integer", nullable: false),
                    ProgressTotal = table.Column<int>(type: "integer", nullable: false),
                    AddedGames = table.Column<int>(type: "integer", nullable: false),
                    AddedExpansions = table.Column<int>(type: "integer", nullable: false),
                    OrphanExpansions = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubBggImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubBggImports_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubBggImports_ActiveClub",
                table: "ClubBggImports",
                column: "ClubId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ClubBggImports_PublicId",
                table: "ClubBggImports",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubBggImports_Status_LeaseExpiresAt_CreatedAt",
                table: "ClubBggImports",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubBggImports");
        }
    }
}
