using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class GatheringPlayRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Participants",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "GameGatheringGuests",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateTable(
                name: "GatheringPlayRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatheringId = table.Column<long>(type: "bigint", nullable: false),
                    WasPlayed = table.Column<bool>(type: "boolean", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    RecordedByParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    GameSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExternalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringPlayRecords", x => x.Id);
                    table.CheckConstraint("CK_PlayRecord_Outcome", "(\"WasPlayed\" AND \"EndedAtUtc\" IS NOT NULL) OR (NOT \"WasPlayed\" AND \"EndedAtUtc\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_GatheringPlayRecords_GameGatherings_GatheringId",
                        column: x => x.GatheringId,
                        principalTable: "GameGatherings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatheringPlayRecords_Participants_RecordedByParticipantId",
                        column: x => x.RecordedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GatheringPlayPlayers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayRecordId = table.Column<long>(type: "bigint", nullable: false),
                    SourcePlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringPlayPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatheringPlayPlayers_GatheringPlayRecords_PlayRecordId",
                        column: x => x.PlayRecordId,
                        principalTable: "GatheringPlayRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GatheringPlayPlayers_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_PublicId",
                table: "Participants",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatheringGuests_PublicId",
                table: "GameGatheringGuests",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatheringPlayPlayers_ParticipantId",
                table: "GatheringPlayPlayers",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GatheringPlayPlayers_PlayRecordId_SourcePlayerId",
                table: "GatheringPlayPlayers",
                columns: new[] { "PlayRecordId", "SourcePlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatheringPlayRecords_GatheringId",
                table: "GatheringPlayRecords",
                column: "GatheringId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatheringPlayRecords_PublicId",
                table: "GatheringPlayRecords",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatheringPlayRecords_RecordedByParticipantId",
                table: "GatheringPlayRecords",
                column: "RecordedByParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Миграция только вперёд: откат удалит сохраняемую историю.");
    }
}
