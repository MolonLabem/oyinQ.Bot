using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Games",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BggId = table.Column<long>(type: "bigint", nullable: true),
                TeseraAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                MinPlayers = table.Column<int>(type: "integer", nullable: true),
                MaxPlayers = table.Column<int>(type: "integer", nullable: true),
                BestPlayers = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Games", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Participants",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                TelegramUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                DaysStaying = table.Column<int>(type: "integer", nullable: true),
                NeedsAccommodation = table.Column<bool>(type: "boolean", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Participants", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CollectionImports",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ParticipantId = table.Column<long>(type: "bigint", nullable: true),
                Target = table.Column<int>(type: "integer", nullable: false),
                Provider = table.Column<int>(type: "integer", nullable: false),
                ExternalUsername = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ProgressJson = table.Column<string>(type: "jsonb", nullable: true),
                AddedCount = table.Column<int>(type: "integer", nullable: false),
                SkippedCount = table.Column<int>(type: "integer", nullable: false),
                Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CollectionImports", x => x.Id);
                table.ForeignKey(
                    name: "FK_CollectionImports_Participants_ParticipantId",
                    column: x => x.ParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "GameCopies",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GameId = table.Column<long>(type: "bigint", nullable: false),
                OwnerParticipantId = table.Column<long>(type: "bigint", nullable: true),
                Source = table.Column<int>(type: "integer", nullable: false),
                BringStatus = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameCopies", x => x.Id);
                table.ForeignKey(
                    name: "FK_GameCopies_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_GameCopies_Participants_OwnerParticipantId",
                    column: x => x.OwnerParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GameInterests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                GameId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameInterests", x => x.Id);
                table.ForeignKey(
                    name: "FK_GameInterests_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_GameInterests_Participants_ParticipantId",
                    column: x => x.ParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GameSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GameId = table.Column<long>(type: "bigint", nullable: false),
                HostParticipantId = table.Column<long>(type: "bigint", nullable: false),
                TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                TelegramMessageId = table.Column<int>(type: "integer", nullable: true),
                WantedAdditionalPlayers = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_GameSessions_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GameSessions_Participants_HostParticipantId",
                    column: x => x.HostParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ParticipantConversationStates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                State = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DataJson = table.Column<string>(type: "jsonb", nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ParticipantConversationStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_ParticipantConversationStates_Participants_ParticipantId",
                    column: x => x.ParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GameSessionParticipants",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GameSessionId = table.Column<long>(type: "bigint", nullable: false),
                ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameSessionParticipants", x => x.Id);
                table.ForeignKey(
                    name: "FK_GameSessionParticipants_GameSessions_GameSessionId",
                    column: x => x.GameSessionId,
                    principalTable: "GameSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_GameSessionParticipants_Participants_ParticipantId",
                    column: x => x.ParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_CollectionImports_ParticipantId", table: "CollectionImports", column: "ParticipantId");
        migrationBuilder.CreateIndex(name: "IX_CollectionImports_Provider_ExternalUsername_ParticipantId_Target_Status", table: "CollectionImports", columns: new[] { "Provider", "ExternalUsername", "ParticipantId", "Target", "Status" });
        migrationBuilder.CreateIndex(name: "IX_CollectionImports_Status_CreatedAt", table: "CollectionImports", columns: new[] { "Status", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_GameCopies_GameId_OwnerParticipantId", table: "GameCopies", columns: new[] { "GameId", "OwnerParticipantId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_GameCopies_OwnerParticipantId", table: "GameCopies", column: "OwnerParticipantId");
        migrationBuilder.CreateIndex(name: "IX_GameInterests_GameId", table: "GameInterests", column: "GameId");
        migrationBuilder.CreateIndex(name: "IX_GameInterests_ParticipantId_GameId", table: "GameInterests", columns: new[] { "ParticipantId", "GameId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_Games_BggId", table: "Games", column: "BggId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Games_NormalizedName", table: "Games", column: "NormalizedName");
        migrationBuilder.CreateIndex(name: "IX_Games_TeseraAlias", table: "Games", column: "TeseraAlias", unique: true);
        migrationBuilder.CreateIndex(name: "IX_GameSessionParticipants_GameSessionId_ParticipantId", table: "GameSessionParticipants", columns: new[] { "GameSessionId", "ParticipantId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_GameSessionParticipants_ParticipantId", table: "GameSessionParticipants", column: "ParticipantId");
        migrationBuilder.CreateIndex(name: "IX_GameSessions_GameId", table: "GameSessions", column: "GameId");
        migrationBuilder.CreateIndex(name: "IX_GameSessions_HostParticipantId", table: "GameSessions", column: "HostParticipantId");
        migrationBuilder.CreateIndex(name: "IX_GameSessions_TelegramChatId_TelegramMessageId", table: "GameSessions", columns: new[] { "TelegramChatId", "TelegramMessageId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ParticipantConversationStates_ExpiresAt", table: "ParticipantConversationStates", column: "ExpiresAt");
        migrationBuilder.CreateIndex(name: "IX_ParticipantConversationStates_ParticipantId", table: "ParticipantConversationStates", column: "ParticipantId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Participants_TelegramUserId", table: "Participants", column: "TelegramUserId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CollectionImports");
        migrationBuilder.DropTable(name: "GameCopies");
        migrationBuilder.DropTable(name: "GameInterests");
        migrationBuilder.DropTable(name: "GameSessionParticipants");
        migrationBuilder.DropTable(name: "ParticipantConversationStates");
        migrationBuilder.DropTable(name: "GameSessions");
        migrationBuilder.DropTable(name: "Games");
        migrationBuilder.DropTable(name: "Participants");
    }
}
