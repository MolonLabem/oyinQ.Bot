using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubCampContextsAndGatheringSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "GameId",
                table: "GameGatherings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "GameSnapshotJson",
                table: "GameGatherings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "GameGatherings" AS gg
                SET "GameSnapshotJson" = jsonb_build_object(
                    'version', 1,
                    'bggId', g."BggId",
                    'name', g."Name",
                    'thumbnailImageUrl', g."ThumbnailImageUrl",
                    'imageUrl', g."ImageUrl",
                    'minPlayers', g."MinPlayers",
                    'maxPlayers', g."MaxPlayers",
                    'bestPlayers', g."BestPlayers",
                    'selectedExpansions', COALESCE((
                        SELECT jsonb_agg(jsonb_build_object('bggId', e."BggId", 'name', e."Name") ORDER BY e."Name")
                        FROM "GameGatheringExpansions" AS e
                        WHERE e."GameGatheringId" = gg."Id"
                    ), '[]'::jsonb))
                FROM "Games" AS g
                WHERE g."Id" = gg."GameId";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "GameSnapshotJson",
                table: "GameGatherings",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OyinQCommunities_Key_Mode",
                table: "OyinQCommunities",
                columns: new[] { "Key", "Mode" });

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotChatKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BotChatMode = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CollectionJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                    table.CheckConstraint("CK_Clubs_BotChatMode", "\"BotChatMode\" = 0");
                    table.ForeignKey(
                        name: "FK_Clubs_OyinQCommunities_BotChatKey_BotChatMode",
                        columns: x => new { x.BotChatKey, x.BotChatMode },
                        principalTable: "OyinQCommunities",
                        principalColumns: new[] { "Key", "Mode" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Camps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotChatKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BotChatMode = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceClubId = table.Column<long>(type: "bigint", nullable: true),
                    BaseCollectionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camps", x => x.Id);
                    table.CheckConstraint("CK_Camps_BotChatMode", "\"BotChatMode\" = 1");
                    table.ForeignKey(
                        name: "FK_Camps_Clubs_SourceClubId",
                        column: x => x.SourceClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Camps_OyinQCommunities_BotChatKey_BotChatMode",
                        columns: x => new { x.BotChatKey, x.BotChatMode },
                        principalTable: "OyinQCommunities",
                        principalColumns: new[] { "Key", "Mode" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Clubs" ("BotChatKey", "BotChatMode", "Name", "CollectionJson", "CreatedAt", "UpdatedAt")
                SELECT c."Key", 0, c."Name",
                    jsonb_build_object('version', 1, 'games', COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'bggId', g."BggId",
                            'name', g."Name",
                            'thumbnailImageUrl', g."ThumbnailImageUrl",
                            'imageUrl', g."ImageUrl",
                            'minPlayers', g."MinPlayers",
                            'maxPlayers', g."MaxPlayers",
                            'bestPlayers', g."BestPlayers",
                            'expansions', '[]'::jsonb) ORDER BY g."Name")
                        FROM "Games" AS g
                        WHERE g."BggId" IS NOT NULL
                          AND EXISTS (
                              SELECT 1 FROM "GameCopies" AS copy
                              WHERE copy."GameId" = g."Id"
                                AND copy."OwnerParticipantId" IS NULL
                                AND copy."Source" = 1)
                    ), '[]'::jsonb)),
                    c."CreatedAt", c."UpdatedAt"
                FROM "OyinQCommunities" AS c
                WHERE c."Mode" = 0;

                INSERT INTO "Camps" ("BotChatKey", "BotChatMode", "Name", "SourceClubId", "BaseCollectionJson", "Status", "CreatedByTelegramUserId", "CreatedAt", "UpdatedAt")
                SELECT c."Key", 1, c."Name", NULL,
                    jsonb_build_object('version', 1, 'games', COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'bggId', g."BggId",
                            'name', g."Name",
                            'thumbnailImageUrl', g."ThumbnailImageUrl",
                            'imageUrl', g."ImageUrl",
                            'minPlayers', g."MinPlayers",
                            'maxPlayers', g."MaxPlayers",
                            'bestPlayers', g."BestPlayers",
                            'expansions', '[]'::jsonb) ORDER BY g."Name")
                        FROM "Games" AS g
                        WHERE g."BggId" IS NOT NULL
                          AND EXISTS (
                              SELECT 1 FROM "GameCopies" AS copy
                              WHERE copy."GameId" = g."Id"
                                AND copy."OwnerParticipantId" IS NULL
                                AND copy."Source" = 1)
                    ), '[]'::jsonb)),
                    1, 0, c."CreatedAt", c."UpdatedAt"
                FROM "OyinQCommunities" AS c
                WHERE c."Mode" = 1;
                """);

            migrationBuilder.CreateTable(
                name: "CampGameContributions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    ParentBggId = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampGameContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampGameContributions_Camps_CampId",
                        column: x => x.CampId,
                        principalTable: "Camps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampGameContributions_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampRegistrations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    DaysStaying = table.Column<int>(type: "integer", nullable: true),
                    NeedsAccommodation = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampRegistrations_Camps_CampId",
                        column: x => x.CampId,
                        principalTable: "Camps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampRegistrations_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO "CampRegistrations" ("CampId", "ParticipantId", "DaysStaying", "NeedsAccommodation", "CreatedAt", "UpdatedAt")
                SELECT camp."Id", participant."Id", participant."DaysStaying", participant."NeedsAccommodation",
                       participant."CreatedAt", participant."UpdatedAt"
                FROM "Camps" AS camp
                CROSS JOIN "Participants" AS participant
                WHERE participant."DaysStaying" IS NOT NULL
                  AND participant."NeedsAccommodation" IS NOT NULL;

                INSERT INTO "CampGameContributions" ("CampId", "ParticipantId", "BggId", "ItemType", "ParentBggId", "SnapshotJson", "CreatedAt", "UpdatedAt")
                SELECT camp."Id", copy."OwnerParticipantId", game."BggId", 0, NULL,
                       jsonb_build_object(
                           'name', game."Name",
                           'thumbnailImageUrl', game."ThumbnailImageUrl",
                           'imageUrl', game."ImageUrl",
                           'minPlayers', game."MinPlayers",
                           'maxPlayers', game."MaxPlayers",
                           'bestPlayers', game."BestPlayers"),
                       copy."CreatedAt", copy."CreatedAt"
                FROM "Camps" AS camp
                CROSS JOIN "GameCopies" AS copy
                JOIN "Games" AS game ON game."Id" = copy."GameId"
                WHERE copy."OwnerParticipantId" IS NOT NULL
                  AND game."BggId" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CampGameContributions_CampId_BggId_ItemType",
                table: "CampGameContributions",
                columns: new[] { "CampId", "BggId", "ItemType" });

            migrationBuilder.CreateIndex(
                name: "IX_CampGameContributions_CampId_ParticipantId_BggId_ItemType",
                table: "CampGameContributions",
                columns: new[] { "CampId", "ParticipantId", "BggId", "ItemType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampGameContributions_ParticipantId",
                table: "CampGameContributions",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampRegistrations_CampId_ParticipantId",
                table: "CampRegistrations",
                columns: new[] { "CampId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampRegistrations_ParticipantId",
                table: "CampRegistrations",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Camps_BotChatKey",
                table: "Camps",
                column: "BotChatKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Camps_BotChatKey_BotChatMode",
                table: "Camps",
                columns: new[] { "BotChatKey", "BotChatMode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Camps_SourceClubId",
                table: "Camps",
                column: "SourceClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_BotChatKey",
                table: "Clubs",
                column: "BotChatKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_BotChatKey_BotChatMode",
                table: "Clubs",
                columns: new[] { "BotChatKey", "BotChatMode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GameGatherings_OyinQCommunities_CommunityKey",
                table: "GameGatherings",
                column: "CommunityKey",
                principalTable: "OyinQCommunities",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "GameGatherings" WHERE "GameId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot downgrade: snapshot-only gatherings have no legacy GameId.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_GameGatherings_OyinQCommunities_CommunityKey",
                table: "GameGatherings");

            migrationBuilder.DropTable(
                name: "CampGameContributions");

            migrationBuilder.DropTable(
                name: "CampRegistrations");

            migrationBuilder.DropTable(
                name: "Camps");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OyinQCommunities_Key_Mode",
                table: "OyinQCommunities");

            migrationBuilder.DropColumn(
                name: "GameSnapshotJson",
                table: "GameGatherings");

            migrationBuilder.AlterColumn<long>(
                name: "GameId",
                table: "GameGatherings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
