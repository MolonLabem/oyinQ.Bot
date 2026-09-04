using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace oyinQ.Bot.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistentParticipantCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampBggImports_ParticipantId",
                table: "CampBggImports");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PrivateChatStartedAt",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CampId",
                table: "CampBggImports",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "ParticipantCollectionItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: false),
                    BggId = table.Column<long>(type: "bigint", nullable: false),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    ParentBggId = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantCollectionItems", x => x.Id);
                    table.CheckConstraint("CK_ParticipantCollectionItems_BggId", "\"BggId\" > 0");
                    table.ForeignKey(
                        name: "FK_ParticipantCollectionItems_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampBggImports_ActiveProfileParticipant",
                table: "CampBggImports",
                column: "ParticipantId",
                unique: true,
                filter: "\"CampId\" IS NULL AND \"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantCollectionItems_ParticipantId_BggId_ItemType",
                table: "ParticipantCollectionItems",
                columns: new[] { "ParticipantId", "BggId", "ItemType" },
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "ParticipantCollectionItems"
                    ("ParticipantId", "BggId", "ItemType", "ParentBggId", "SnapshotJson", "Source", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT ON ("ParticipantId", "BggId", "ItemType")
                    "ParticipantId", "BggId", "ItemType", "ParentBggId", "SnapshotJson", "Source", "CreatedAt", "UpdatedAt"
                FROM "CampGameContributions"
                WHERE "BggId" > 0
                ORDER BY "ParticipantId", "BggId", "ItemType", ("Source" = 2) DESC, "UpdatedAt" DESC, "Id" DESC
                ON CONFLICT ("ParticipantId", "BggId", "ItemType") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Миграция только вперёд: откат удалит личные коллекции и профильные задания.");
    }
}
