using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGatheringPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Games",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailImageUrl",
                table: "Games",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameGatherings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommunityKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizerParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MinimumPlayers = table.Column<int>(type: "integer", nullable: false),
                    DesiredPlayers = table.Column<int>(type: "integer", nullable: false),
                    MaximumPlayers = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CanTeachRules = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGatherings", x => x.Id);
                    table.CheckConstraint("CK_GameGatherings_PlayerLimits", "\"MinimumPlayers\" >= 1 AND \"MinimumPlayers\" <= \"DesiredPlayers\" AND \"DesiredPlayers\" <= \"MaximumPlayers\"");
                    table.ForeignKey(
                        name: "FK_GameGatherings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameGatherings_Participants_OrganizerParticipantId",
                        column: x => x.OrganizerParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameGatheringExpansions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameGatheringId = table.Column<long>(type: "bigint", nullable: false),
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGatheringExpansions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameGatheringExpansions_GameGatherings_GameGatheringId",
                        column: x => x.GameGatheringId,
                        principalTable: "GameGatherings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameGatheringParticipants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameGatheringId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttendanceOutcome = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGatheringParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameGatheringParticipants_GameGatherings_GameGatheringId",
                        column: x => x.GameGatheringId,
                        principalTable: "GameGatherings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGatheringParticipants_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringExpansions_GameGatheringId_BggId",
                table: "GameGatheringExpansions",
                columns: new[] { "GameGatheringId", "BggId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringParticipants_GameGatheringId_ParticipantId",
                table: "GameGatheringParticipants",
                columns: new[] { "GameGatheringId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringParticipants_GameGatheringId_Status_JoinedAt",
                table: "GameGatheringParticipants",
                columns: new[] { "GameGatheringId", "Status", "JoinedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringParticipants_ParticipantId",
                table: "GameGatheringParticipants",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_CommunityKey_StartsAtUtc",
                table: "GameGatherings",
                columns: new[] { "CommunityKey", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_GameId",
                table: "GameGatherings",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_OrganizerParticipantId",
                table: "GameGatherings",
                column: "OrganizerParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_PublicId",
                table: "GameGatherings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_TelegramChatId_TelegramMessageId",
                table: "GameGatherings",
                columns: new[] { "TelegramChatId", "TelegramMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameGatheringExpansions");

            migrationBuilder.DropTable(
                name: "GameGatheringParticipants");

            migrationBuilder.DropTable(
                name: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ThumbnailImageUrl",
                table: "Games");
        }
    }
}
