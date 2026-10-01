using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

public partial class GatheringParticipantDisplayNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("DisplayNameOverride", "GameGatheringParticipants",
            type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("OrganizerDisplayNameOverride", "GameGatherings",
            type: "character varying(128)", maxLength: 128, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("DisplayNameOverride", "GameGatheringParticipants");
        migrationBuilder.DropColumn("OrganizerDisplayNameOverride", "GameGatherings");
    }
}
