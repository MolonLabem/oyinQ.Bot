using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayOutcomesReferencesAndReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "LegacyPlayOutcomeJson", table: "GameGatherings", type: "jsonb", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "ConfirmedWasPlayed",
                table: "GameGatherings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OutcomeRecordedAt",
                table: "GameGatherings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OutcomeRecordedByParticipantId",
                table: "GameGatherings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutcomeRevision",
                table: "GameGatherings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GatheringExternalPlayReferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GatheringPlayRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AddedByParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringExternalPlayReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatheringExternalPlayReferences_GatheringPlayRecords_Gather~",
                        column: x => x.GatheringPlayRecordId,
                        principalTable: "GatheringPlayRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatheringExternalPlayReferences_Participants_AddedByPartici~",
                        column: x => x.AddedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseAnnouncements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: false),
                    CreatedByParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseAnnouncements_Participants_CreatedByParticipantId",
                        column: x => x.CreatedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseAnnouncementDeliveries",
                columns: table => new
                {
                    ReleaseId = table.Column<string>(type: "character varying(64)", nullable: false),
                    CommunityKey = table.Column<string>(type: "character varying(32)", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAnnouncementDeliveries", x => new { x.ReleaseId, x.CommunityKey });
                    table.ForeignKey(
                        name: "FK_ReleaseAnnouncementDeliveries_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseAnnouncementDeliveries_ReleaseAnnouncements_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ReleaseAnnouncements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatheringExternalPlayReferences_AddedByParticipantId",
                table: "GatheringExternalPlayReferences",
                column: "AddedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GatheringExternalPlayReferences_GatheringPlayRecordId_Url",
                table: "GatheringExternalPlayReferences",
                columns: new[] { "GatheringPlayRecordId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAnnouncementDeliveries_CommunityKey",
                table: "ReleaseAnnouncementDeliveries",
                column: "CommunityKey");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAnnouncementDeliveries_State_AttemptedAt",
                table: "ReleaseAnnouncementDeliveries",
                columns: new[] { "State", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAnnouncements_CreatedByParticipantId",
                table: "ReleaseAnnouncements",
                column: "CreatedByParticipantId");
            // Preserve the previous outcome audit before separating non-plays from actual plays.
            migrationBuilder.Sql("""
                UPDATE "GameGatherings" g SET
                    "ConfirmedWasPlayed" = p."WasPlayed", "OutcomeRecordedAt" = p."UpdatedAt",
                    "OutcomeRecordedByParticipantId" = p."RecordedByParticipantId", "OutcomeRevision" = p."Revision",
                    "LegacyPlayOutcomeJson" = CASE WHEN NOT p."WasPlayed" THEN
                        to_jsonb(p) || jsonb_build_object('players', (SELECT coalesce(jsonb_agg(to_jsonb(r)), '[]'::jsonb) FROM "GatheringPlayPlayers" r WHERE r."PlayRecordId" = p."Id"))
                        ELSE NULL END
                FROM "GatheringPlayRecords" p WHERE p."GatheringId" = g."Id";
                INSERT INTO "GatheringExternalPlayReferences" ("GatheringPlayRecordId", "Provider", "Url", "AddedByParticipantId", "CreatedAt")
                SELECT "Id", 0, trim("ExternalUrl"), "RecordedByParticipantId", "RecordedAt"
                FROM "GatheringPlayRecords"
                WHERE "WasPlayed" AND trim("ExternalUrl") ~ '^https://app[.]bgstatsapp[.]com/'
                    AND "ExternalUrl" !~ '[[:cntrl:]]'
                ON CONFLICT DO NOTHING;
                DELETE FROM "GatheringPlayRecords" WHERE NOT "WasPlayed";
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Миграция сохраняет историю и поддерживает только движение вперёд.");
    }
}
