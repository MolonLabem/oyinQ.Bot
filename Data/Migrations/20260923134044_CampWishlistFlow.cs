using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CampWishlistFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ActorParticipantId",
                table: "Notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BggId",
                table: "Notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareCollection",
                table: "CampRegistrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShareWishes",
                table: "CampRegistrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly[]>(
                name: "AvailableDates",
                table: "CampGameContributions",
                type: "date[]",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampBringRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    Declined = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampBringRequests", x => x.Id);
                    table.CheckConstraint("CK_CampBringRequest_BggId", "\"BggId\" > 0");
                    table.ForeignKey(
                        name: "FK_CampBringRequests_Camps_CampId",
                        column: x => x.CampId,
                        principalTable: "Camps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampBringRequests_Participants_OwnerParticipantId",
                        column: x => x.OwnerParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampBringRequesters",
                columns: table => new
                {
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    Dates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampBringRequesters", x => new { x.RequestId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_CampBringRequesters_CampBringRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "CampBringRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampBringRequesters_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampBringRequesters_ParticipantId_CreatedAt",
                table: "CampBringRequesters",
                columns: new[] { "ParticipantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampBringRequests_CampId_OwnerParticipantId_BggId",
                table: "CampBringRequests",
                columns: new[] { "CampId", "OwnerParticipantId", "BggId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampBringRequests_OwnerParticipantId",
                table: "CampBringRequests",
                column: "OwnerParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampBringRequesters");

            migrationBuilder.DropTable(
                name: "CampBringRequests");

            migrationBuilder.DropColumn(
                name: "ActorParticipantId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BggId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ShareCollection",
                table: "CampRegistrations");

            migrationBuilder.DropColumn(
                name: "ShareWishes",
                table: "CampRegistrations");

            migrationBuilder.DropColumn(
                name: "AvailableDates",
                table: "CampGameContributions");
        }
    }
}
