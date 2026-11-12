using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class BggRatingCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BggRatingCache",
                columns: table => new
                {
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    Rating = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Failed = table.Column<bool>(type: "boolean", nullable: false),
                    RefreshRequested = table.Column<bool>(type: "boolean", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BggRatingCache", x => x.BggId);
                    table.CheckConstraint("CK_BggRatingCache_Rating", "\"Rating\" IS NULL OR (\"Rating\" >= 1 AND \"Rating\" <= 10)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BggRatingCache_RefreshRequested_RefreshAfter",
                table: "BggRatingCache",
                columns: new[] { "RefreshRequested", "RefreshAfter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BggRatingCache");
        }
    }
}
