using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnownTelegramChats",
                columns: table => new
                {
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsBotPresent = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownTelegramChats", x => x.TelegramChatId);
                });

            migrationBuilder.CreateTable(
                name: "OyinQCommunities",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OyinQCommunities", x => x.Key);
                    table.UniqueConstraint("AK_OyinQCommunities_Key_Mode", x => new { x.Key, x.Mode });
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
                    PreferredDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ActiveCommunityKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingTelegramPeerSelections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    RequestedByTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PreparedButtonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingTelegramPeerSelections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramMessageCleanups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramMessageCleanups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatAdminPermissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommunityKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TelegramUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GrantedByTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAdminPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatAdminPermissions_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    CollectionRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
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
                name: "GameGatherings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommunityKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GameSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
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
                    PublicationStatus = table.Column<int>(type: "integer", nullable: false),
                    PublicationError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublicationAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastPublicationAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                        name: "FK_GameGatherings_OyinQCommunities_CommunityKey",
                        column: x => x.CommunityKey,
                        principalTable: "OyinQCommunities",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameGatherings_Participants_OrganizerParticipantId",
                        column: x => x.OrganizerParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
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
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camps", x => x.Id);
                    table.CheckConstraint("CK_Camps_BotChatMode", "\"BotChatMode\" = 1");
                    table.CheckConstraint("CK_Camps_DateRange", "(\"StartDate\" IS NULL AND \"EndDate\" IS NULL) OR (\"StartDate\" IS NOT NULL AND \"EndDate\" IS NOT NULL AND \"StartDate\" <= \"EndDate\")");
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

            migrationBuilder.CreateTable(
                name: "ClubMetadataRefreshes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BggIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProgressCurrent = table.Column<int>(type: "integer", nullable: false),
                    ProgressTotal = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMetadataRefreshes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubMetadataRefreshes_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "CampBggImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    BggUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProgressCurrent = table.Column<int>(type: "integer", nullable: false),
                    ProgressTotal = table.Column<int>(type: "integer", nullable: true),
                    DraftJson = table.Column<string>(type: "jsonb", nullable: true),
                    ConfirmationJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OverrideResolution = table.Column<int>(type: "integer", nullable: true),
                    OverrideResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampBggImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampBggImports_Camps_CampId",
                        column: x => x.CampId,
                        principalTable: "Camps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampBggImports_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Commitment = table.Column<int>(type: "integer", nullable: false),
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
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "CampRegistrationDays",
                columns: table => new
                {
                    CampRegistrationId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampRegistrationDays", x => new { x.CampRegistrationId, x.Date });
                    table.ForeignKey(
                        name: "FK_CampRegistrationDays_CampRegistrations_CampRegistrationId",
                        column: x => x.CampRegistrationId,
                        principalTable: "CampRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_ActiveCampParticipant",
                table: "CampBggImports",
                columns: new[] { "CampId", "ParticipantId" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_CampId_ParticipantId_UpdatedAt",
                table: "CampBggImports",
                columns: new[] { "CampId", "ParticipantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_ParticipantId",
                table: "CampBggImports",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_PublicId",
                table: "CampBggImports",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_Status_LeaseExpiresAt_CreatedAt",
                table: "CampBggImports",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });

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
                name: "IX_Camps_Status_EndDate_Id",
                table: "Camps",
                columns: new[] { "Status", "EndDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAdminPermissions_CommunityKey_RevokedAt",
                table: "ChatAdminPermissions",
                columns: new[] { "CommunityKey", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAdminPermissions_TelegramUserId_CommunityKey",
                table: "ChatAdminPermissions",
                columns: new[] { "TelegramUserId", "CommunityKey" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_ActiveClub",
                table: "ClubMetadataRefreshes",
                column: "ClubId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_PublicId",
                table: "ClubMetadataRefreshes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubMetadataRefreshes_Status_LeaseExpiresAt_CreatedAt",
                table: "ClubMetadataRefreshes",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });

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
                name: "IX_GameGatherings_OrganizerParticipantId",
                table: "GameGatherings",
                column: "OrganizerParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_PublicId",
                table: "GameGatherings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_Status_StartsAtUtc_Id",
                table: "GameGatherings",
                columns: new[] { "Status", "StartsAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GameGatherings_TelegramChatId_TelegramMessageId",
                table: "GameGatherings",
                columns: new[] { "TelegramChatId", "TelegramMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnownTelegramChats_IsBotPresent",
                table: "KnownTelegramChats",
                column: "IsBotPresent");

            migrationBuilder.CreateIndex(
                name: "IX_OyinQCommunities_TelegramChatId",
                table: "OyinQCommunities",
                column: "TelegramChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_TelegramUserId",
                table: "Participants",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingTelegramPeerSelections_PublicId",
                table: "PendingTelegramPeerSelections",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingTelegramPeerSelections_RequestedByTelegramUserId_Sta~",
                table: "PendingTelegramPeerSelections",
                columns: new[] { "RequestedByTelegramUserId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingTelegramPeerSelections_RequestId",
                table: "PendingTelegramPeerSelections",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessageCleanups_LastAttemptAt_CreatedAt",
                table: "TelegramMessageCleanups",
                columns: new[] { "LastAttemptAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessageCleanups_TelegramChatId_TelegramMessageId",
                table: "TelegramMessageCleanups",
                columns: new[] { "TelegramChatId", "TelegramMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampBggImports");

            migrationBuilder.DropTable(
                name: "CampGameContributions");

            migrationBuilder.DropTable(
                name: "CampRegistrationDays");

            migrationBuilder.DropTable(
                name: "ChatAdminPermissions");

            migrationBuilder.DropTable(
                name: "ClubBggImports");

            migrationBuilder.DropTable(
                name: "ClubMetadataRefreshes");

            migrationBuilder.DropTable(
                name: "GameGatheringExpansions");

            migrationBuilder.DropTable(
                name: "GameGatheringParticipants");

            migrationBuilder.DropTable(
                name: "KnownTelegramChats");

            migrationBuilder.DropTable(
                name: "PendingTelegramPeerSelections");

            migrationBuilder.DropTable(
                name: "TelegramMessageCleanups");

            migrationBuilder.DropTable(
                name: "CampRegistrations");

            migrationBuilder.DropTable(
                name: "GameGatherings");

            migrationBuilder.DropTable(
                name: "Camps");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "OyinQCommunities");
        }
    }
}
