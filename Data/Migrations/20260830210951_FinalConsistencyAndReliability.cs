using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinalConsistencyAndReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMetadataRefreshes_ClubId",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropIndex(
                name: "IX_ClubMetadataRefreshes_Status_CreatedAt",
                table: "ClubMetadataRefreshes");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "ClubMetadataRefreshes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "ClubMetadataRefreshes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationJson",
                table: "CampBggImports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ClubMetadataRefreshes"
                SET "Status" = 0, "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "Status" = 1;

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "ClubId" ORDER BY "CreatedAt", "Id") AS rn
                    FROM "ClubMetadataRefreshes"
                    WHERE "Status" IN (0, 1)
                )
                UPDATE "ClubMetadataRefreshes" AS jobs
                SET "Status" = 3,
                    "Error" = 'Superseded while enforcing one active metadata refresh per Club.',
                    "UpdatedAt" = CURRENT_TIMESTAMP
                FROM ranked
                WHERE jobs."Id" = ranked."Id" AND ranked.rn > 1;

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "CampId", "ParticipantId" ORDER BY "CreatedAt", "Id") AS rn
                    FROM "CampBggImports"
                    WHERE "Status" IN (0, 1)
                )
                UPDATE "CampBggImports" AS imports
                SET "Status" = 5,
                    "CancellationRequestedAt" = COALESCE("CancellationRequestedAt", CURRENT_TIMESTAMP),
                    "UpdatedAt" = CURRENT_TIMESTAMP
                FROM ranked
                WHERE imports."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_ActiveClub",
                table: "ClubMetadataRefreshes",
                column: "ClubId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_Status_LeaseExpiresAt_CreatedAt",
                table: "ClubMetadataRefreshes",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Camps_Status_EndDate_Id",
                table: "Camps",
                columns: new[] { "Status", "EndDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_ActiveCampParticipant",
                table: "CampBggImports",
                columns: new[] { "CampId", "ParticipantId" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMetadataRefreshes_ActiveClub",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropIndex(
                name: "IX_ClubMetadataRefreshes_Status_LeaseExpiresAt_CreatedAt",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropIndex(
                name: "IX_Camps_Status_EndDate_Id",
                table: "Camps");

            migrationBuilder.DropIndex(
                name: "IX_CampBggImports_ActiveCampParticipant",
                table: "CampBggImports");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "ClubMetadataRefreshes");

            migrationBuilder.DropColumn(
                name: "ConfirmationJson",
                table: "CampBggImports");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_ClubId",
                table: "ClubMetadataRefreshes",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_Status_CreatedAt",
                table: "ClubMetadataRefreshes",
                columns: new[] { "Status", "CreatedAt" });
        }
    }
}
