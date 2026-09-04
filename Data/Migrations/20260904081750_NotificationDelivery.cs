using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TelegramDeliveryBlockedAt",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    GatheringFull = table.Column<bool>(type: "boolean", nullable: false),
                    GatheringDetailsChanged = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizerParticipantLeft = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizerReplacement = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizerBelowMinimum = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizerMissingProvider = table.Column<bool>(type: "boolean", nullable: false),
                    ImportCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderLeadMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.ParticipantId);
                    table.CheckConstraint("CK_NotificationPreferences_Reminder", "\"ReminderLeadMinutes\" IN (0, 30, 60, 120, 360, 720, 1440)");
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    GatheringPublicId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommunityKey = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ImportPublicId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DeduplicationKey",
                table: "Notifications",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_GatheringPublicId_ParticipantId",
                table: "Notifications",
                columns: new[] { "GatheringPublicId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ParticipantId",
                table: "Notifications",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_State_NextAttemptAt",
                table: "Notifications",
                columns: new[] { "State", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Миграция только вперёд: откат удалит сохраняемую историю.");
    }
}
