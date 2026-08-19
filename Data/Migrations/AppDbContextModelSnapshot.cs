using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

[DbContext(typeof(AppDbContext))]
public partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => BuildModelStatic(modelBuilder);

    internal static void BuildModelStatic(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.10")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity<Participant>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<int?>("DaysStaying").HasColumnType("integer");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<bool?>("NeedsAccommodation").HasColumnType("boolean");
            b.Property<string>("PreferredDisplayName").HasMaxLength(128).HasColumnType("character varying(128)");
            b.Property<long>("TelegramUserId").HasColumnType("bigint");
            b.Property<string>("TelegramUsername").HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("TelegramUserId").IsUnique();
            b.ToTable("Participants");
        });

        modelBuilder.Entity<Game>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<string>("BestPlayers").HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<long?>("BggId").HasColumnType("bigint");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ExternalUrl").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<int?>("MaxPlayers").HasColumnType("integer");
            b.Property<int?>("MinPlayers").HasColumnType("integer");
            b.Property<string>("Name").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<string>("NormalizedName").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<string>("TeseraAlias").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("BggId").IsUnique();
            b.HasIndex("NormalizedName");
            b.HasIndex("TeseraAlias").IsUnique();
            b.ToTable("Games");
        });

        modelBuilder.Entity<GameCopy>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<BringStatus>("BringStatus").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<long>("GameId").HasColumnType("bigint");
            b.Property<long?>("OwnerParticipantId").HasColumnType("bigint");
            b.Property<GameCopySource>("Source").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("GameId").IsUnique().HasDatabaseName("IX_GameCopies_GameId_Club").HasFilter("\"OwnerParticipantId\" IS NULL AND \"Source\" = 1");
            b.HasIndex("GameId", "OwnerParticipantId").IsUnique();
            b.HasIndex("OwnerParticipantId");
            b.ToTable("GameCopies");
        });

        modelBuilder.Entity<GameInterest>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<long>("GameId").HasColumnType("bigint");
            b.Property<long>("ParticipantId").HasColumnType("bigint");
            b.HasKey("Id");
            b.HasIndex("GameId");
            b.HasIndex("ParticipantId", "GameId").IsUnique();
            b.ToTable("GameInterests");
        });

        modelBuilder.Entity<GameSession>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<DateTimeOffset?>("ClosedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<long>("GameId").HasColumnType("bigint");
            b.Property<long>("HostParticipantId").HasColumnType("bigint");
            b.Property<SessionStatus>("Status").HasColumnType("integer");
            b.Property<long?>("TelegramChatId").HasColumnType("bigint");
            b.Property<int?>("TelegramMessageId").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<int>("WantedAdditionalPlayers").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("GameId");
            b.HasIndex("HostParticipantId");
            b.HasIndex("TelegramChatId", "TelegramMessageId").IsUnique();
            b.ToTable("GameSessions");
        });

        modelBuilder.Entity<GameSessionParticipant>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<long>("GameSessionId").HasColumnType("bigint");
            b.Property<DateTimeOffset>("JoinedAt").HasColumnType("timestamp with time zone");
            b.Property<long>("ParticipantId").HasColumnType("bigint");
            b.HasKey("Id");
            b.HasIndex("GameSessionId", "ParticipantId").IsUnique();
            b.HasIndex("ParticipantId");
            b.ToTable("GameSessionParticipants");
        });

        modelBuilder.Entity<CollectionImport>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<int>("AddedCount").HasColumnType("integer");
            b.Property<DateTimeOffset?>("CompletedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Error").HasMaxLength(2000).HasColumnType("character varying(2000)");
            b.Property<string>("ExternalUsername").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<long?>("ParticipantId").HasColumnType("bigint");
            b.Property<string>("ProgressJson").HasColumnType("jsonb");
            b.Property<ExternalGameProvider>("Provider").HasColumnType("integer");
            b.Property<long>("RequestedByTelegramUserId").HasColumnType("bigint");
            b.Property<int>("SkippedCount").HasColumnType("integer");
            b.Property<DateTimeOffset?>("StartedAt").HasColumnType("timestamp with time zone");
            b.Property<ImportStatus>("Status").HasColumnType("integer");
            b.Property<ImportTarget>("Target").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("ParticipantId");
            b.HasIndex("RequestedByTelegramUserId");
            b.HasIndex("Provider", "ExternalUsername", "ParticipantId", "Target", "Status");
            b.HasIndex("Status", "CreatedAt");
            b.ToTable("CollectionImports");
        });

        modelBuilder.Entity<ParticipantConversationState>(b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            b.Property<string>("DataJson").HasColumnType("jsonb");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<long>("ParticipantId").HasColumnType("bigint");
            b.Property<string>("State").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("ExpiresAt");
            b.HasIndex("ParticipantId").IsUnique();
            b.ToTable("ParticipantConversationStates");
        });

        modelBuilder.Entity<CollectionImport>(b =>
        {
            b.HasOne<Participant>("Participant").WithMany("CollectionImports").HasForeignKey("ParticipantId").OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GameCopy>(b =>
        {
            b.HasOne<Game>("Game").WithMany("Copies").HasForeignKey("GameId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne<Participant>("OwnerParticipant").WithMany("GameCopies").HasForeignKey("OwnerParticipantId").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameInterest>(b =>
        {
            b.HasOne<Game>("Game").WithMany("Interests").HasForeignKey("GameId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne<Participant>("Participant").WithMany("GameInterests").HasForeignKey("ParticipantId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity<GameSession>(b =>
        {
            b.HasOne<Game>("Game").WithMany("Sessions").HasForeignKey("GameId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne<Participant>("HostParticipant").WithMany("HostedGameSessions").HasForeignKey("HostParticipantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });

        modelBuilder.Entity<GameSessionParticipant>(b =>
        {
            b.HasOne<GameSession>("GameSession").WithMany("Participants").HasForeignKey("GameSessionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne<Participant>("Participant").WithMany("GameSessionParticipations").HasForeignKey("ParticipantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });

        modelBuilder.Entity<ParticipantConversationState>(b =>
        {
            b.HasOne<Participant>("Participant").WithOne("ConversationState").HasForeignKey<ParticipantConversationState>("ParticipantId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
#pragma warning restore 612, 618
    }
}
