using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class WishlistAndRecruitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRecruitmentDigestAt",
                table: "OyinQCommunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecruitmentCooldownHours",
                table: "OyinQCommunities",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            // Existing preference rows opt in too; CLR defaults only cover newly created rows.
            migrationBuilder.AddColumn<bool>(
                name: "WishlistGathering",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "GameWishes",
                columns: table => new
                {
                    CommunityKey = table.Column<string>(type: "character varying(32)", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameWishes", x => new { x.CommunityKey, x.ParticipantId, x.BggId });
                    table.CheckConstraint("CK_GameWish_BggId", "\"BggId\" > 0");
                    table.ForeignKey(
                        name: "FK_GameWishes_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameWishes_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecruitmentDigests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommunityKey = table.Column<string>(type: "character varying(32)", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentDigests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentDigests_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Community_RecruitmentCooldown",
                table: "OyinQCommunities",
                sql: "\"RecruitmentCooldownHours\" BETWEEN 1 AND 24");

            migrationBuilder.CreateIndex(
                name: "IX_GameWishes_CommunityKey_BggId",
                table: "GameWishes",
                columns: new[] { "CommunityKey", "BggId" });

            migrationBuilder.CreateIndex(
                name: "IX_GameWishes_ParticipantId",
                table: "GameWishes",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentDigests_CommunityKey",
                table: "RecruitmentDigests",
                column: "CommunityKey",
                unique: true,
                filter: "\"State\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentDigests_State_RequestedAt",
                table: "RecruitmentDigests",
                columns: new[] { "State", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameWishes");

            migrationBuilder.DropTable(
                name: "RecruitmentDigests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Community_RecruitmentCooldown",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "LastRecruitmentDigestAt",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "RecruitmentCooldownHours",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "WishlistGathering",
                table: "NotificationPreferences");
        }
    }
}
