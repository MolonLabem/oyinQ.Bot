using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class StabilizeClubCampMiniApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "OyinQAdministrators",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUsername",
                table: "OyinQAdministrators",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPublicationAttemptAt",
                table: "GameGatherings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicationAttempts",
                table: "GameGatherings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PublicationError",
                table: "GameGatherings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicationStatus",
                table: "GameGatherings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CollectionRevision",
                table: "Clubs",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("""
                UPDATE "GameGatherings"
                SET "PublicationStatus" = 1
                WHERE "TelegramChatId" IS NOT NULL AND "TelegramMessageId" IS NOT NULL;
                """);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Camps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Camps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "CampGameContributions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "CampGameContributions"
                SET "SnapshotJson" = jsonb_set("SnapshotJson", '{version}', '1'::jsonb, true)
                WHERE NOT ("SnapshotJson" ? 'version');
                """);

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
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_Camps_DateRange",
                table: "Camps",
                sql: "(\"StartDate\" IS NULL AND \"EndDate\" IS NULL) OR (\"StartDate\" IS NOT NULL AND \"EndDate\" IS NOT NULL AND \"StartDate\" <= \"EndDate\")");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampBggImports");

            migrationBuilder.DropTable(
                name: "PendingTelegramPeerSelections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Camps_DateRange",
                table: "Camps");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "OyinQAdministrators");

            migrationBuilder.DropColumn(
                name: "TelegramUsername",
                table: "OyinQAdministrators");

            migrationBuilder.DropColumn(
                name: "LastPublicationAttemptAt",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationAttempts",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationError",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "GameGatherings");

            migrationBuilder.DropColumn(
                name: "CollectionRevision",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Camps");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Camps");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "CampGameContributions");
        }
    }
}
