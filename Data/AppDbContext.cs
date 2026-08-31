using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<OyinQCommunity> OyinQCommunities => Set<OyinQCommunity>();
    public DbSet<OyinQAdministrator> OyinQAdministrators => Set<OyinQAdministrator>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Camp> Camps => Set<Camp>();
    public DbSet<CampRegistration> CampRegistrations => Set<CampRegistration>();
    public DbSet<CampRegistrationDay> CampRegistrationDays => Set<CampRegistrationDay>();
    public DbSet<CampGameContribution> CampGameContributions => Set<CampGameContribution>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameCopy> GameCopies => Set<GameCopy>();
    public DbSet<GameInterest> GameInterests => Set<GameInterest>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameSessionParticipant> GameSessionParticipants => Set<GameSessionParticipant>();
    public DbSet<GameGathering> GameGatherings => Set<GameGathering>();
    public DbSet<GameGatheringExpansion> GameGatheringExpansions => Set<GameGatheringExpansion>();
    public DbSet<GameGatheringParticipant> GameGatheringParticipants => Set<GameGatheringParticipant>();
    public DbSet<CampBggImport> CampBggImports => Set<CampBggImport>();
    public DbSet<ClubMetadataRefresh> ClubMetadataRefreshes => Set<ClubMetadataRefresh>();
    public DbSet<ClubBggImport> ClubBggImports => Set<ClubBggImport>();
    public DbSet<PendingTelegramPeerSelection> PendingTelegramPeerSelections => Set<PendingTelegramPeerSelection>();
    public DbSet<TelegramMessageCleanup> TelegramMessageCleanups => Set<TelegramMessageCleanup>();
    public DbSet<CollectionImport> CollectionImports => Set<CollectionImport>();
    public DbSet<ParticipantConversationState> ParticipantConversationStates => Set<ParticipantConversationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OyinQCommunity>(entity =>
        {
            entity.ToTable("OyinQCommunities");
            entity.HasKey(x => x.Key);
            entity.HasIndex(x => x.TelegramChatId).IsUnique();
            entity.HasAlternateKey(x => new { x.Key, x.Mode });
            entity.Property(x => x.Key).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100);
        });

        modelBuilder.Entity<OyinQAdministrator>(entity =>
        {
            entity.ToTable("OyinQAdministrators");
            entity.HasKey(x => x.TelegramUserId);
            entity.Property(x => x.TelegramUserId).ValueGeneratedNever();
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.TelegramUsername).HasMaxLength(64);
        });

        modelBuilder.Entity<Club>(entity =>
        {
            entity.ToTable("Clubs", table =>
                table.HasCheckConstraint("CK_Clubs_BotChatMode", "\"BotChatMode\" = 0"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BotChatKey).IsUnique();
            entity.Property(x => x.BotChatKey).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.BggUsername).HasMaxLength(100);
            entity.Property(x => x.CollectionJson).HasColumnType("jsonb");
            entity.Property(x => x.CollectionRevision).HasDefaultValue(1L);

            entity.HasOne(x => x.BotChat)
                .WithOne(x => x.Club)
                .HasForeignKey<Club>(x => new { x.BotChatKey, x.BotChatMode })
                .HasPrincipalKey<OyinQCommunity>(x => new { x.Key, x.Mode })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Camp>(entity =>
        {
            entity.ToTable("Camps", table =>
            {
                table.HasCheckConstraint("CK_Camps_BotChatMode", "\"BotChatMode\" = 1");
                table.HasCheckConstraint(
                    "CK_Camps_DateRange",
                    "(\"StartDate\" IS NULL AND \"EndDate\" IS NULL) OR (\"StartDate\" IS NOT NULL AND \"EndDate\" IS NOT NULL AND \"StartDate\" <= \"EndDate\")");
            });
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BotChatKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.EndDate, x.Id });
            entity.Property(x => x.BotChatKey).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.BaseCollectionJson).HasColumnType("jsonb");

            entity.HasOne(x => x.BotChat)
                .WithOne(x => x.Camp)
                .HasForeignKey<Camp>(x => new { x.BotChatKey, x.BotChatMode })
                .HasPrincipalKey<OyinQCommunity>(x => new { x.Key, x.Mode })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SourceClub)
                .WithMany(x => x.SourceCamps)
                .HasForeignKey(x => x.SourceClubId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CampRegistration>(entity =>
        {
            entity.ToTable("CampRegistrations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CampId, x.ParticipantId }).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(128);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.HasOne(x => x.Camp)
                .WithMany(x => x.Registrations)
                .HasForeignKey(x => x.CampId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Participant)
                .WithMany(x => x.CampRegistrations)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CampRegistrationDay>(entity =>
        {
            entity.ToTable("CampRegistrationDays");
            entity.HasKey(x => new { x.CampRegistrationId, x.Date });
            entity.HasOne(x => x.CampRegistration)
                .WithMany(x => x.SelectedDays)
                .HasForeignKey(x => x.CampRegistrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CampGameContribution>(entity =>
        {
            entity.ToTable("CampGameContributions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CampId, x.ParticipantId, x.BggId, x.ItemType }).IsUnique();
            entity.HasIndex(x => new { x.CampId, x.BggId, x.ItemType });
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            entity.HasOne(x => x.Camp)
                .WithMany(x => x.Contributions)
                .HasForeignKey(x => x.CampId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Participant)
                .WithMany(x => x.CampGameContributions)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CampBggImport>(entity =>
        {
            entity.ToTable("CampBggImports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });
            entity.HasIndex(x => new { x.CampId, x.ParticipantId, x.UpdatedAt });
            entity.HasIndex(x => new { x.CampId, x.ParticipantId }).IsUnique()
                .HasDatabaseName("IX_CampBggImports_ActiveCampParticipant")
                .HasFilter("\"Status\" IN (0, 1)");
            entity.Property(x => x.BggUsername).HasMaxLength(100);
            entity.Property(x => x.DraftJson).HasColumnType("jsonb");
            entity.Property(x => x.ConfirmationJson).HasColumnType("jsonb");
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasOne(x => x.Camp)
                .WithMany(x => x.BggImports)
                .HasForeignKey(x => x.CampId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Participant)
                .WithMany()
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClubMetadataRefresh>(entity =>
        {
            entity.ToTable("ClubMetadataRefreshes");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });
            entity.HasIndex(x => x.ClubId).IsUnique()
                .HasDatabaseName("IX_ClubMetadataRefreshes_ActiveClub")
                .HasFilter("\"Status\" IN (0, 1)");
            entity.Property(x => x.BggIdsJson).HasColumnType("jsonb");
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasOne(x => x.Club).WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClubBggImport>(entity =>
        {
            entity.ToTable("ClubBggImports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });
            entity.HasIndex(x => x.ClubId).IsUnique()
                .HasDatabaseName("IX_ClubBggImports_ActiveClub")
                .HasFilter("\"Status\" IN (0, 1)");
            entity.Property(x => x.BggUsername).HasMaxLength(100);
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasOne(x => x.Club).WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PendingTelegramPeerSelection>(entity =>
        {
            entity.ToTable("PendingTelegramPeerSelections");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => new { x.RequestedByTelegramUserId, x.Status, x.ExpiresAt });
            entity.Property(x => x.PreparedButtonId).HasMaxLength(256);
            entity.Property(x => x.ResultJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TelegramMessageCleanup>(entity =>
        {
            entity.ToTable("TelegramMessageCleanups");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TelegramChatId, x.TelegramMessageId }).IsUnique();
            entity.HasIndex(x => new { x.LastAttemptAt, x.CreatedAt });
            entity.Property(x => x.LastError).HasMaxLength(2000);
        });

        modelBuilder.Entity<Participant>(entity =>
        {
            entity.ToTable("Participants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TelegramUserId).IsUnique();
            entity.Property(x => x.TelegramUsername).HasMaxLength(64);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.PreferredDisplayName).HasMaxLength(128);
            entity.Property(x => x.ActiveCommunityKey).HasMaxLength(32);
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("Games");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BggId).IsUnique();
            entity.HasIndex(x => x.NormalizedName);

            entity.Property(x => x.Name).HasMaxLength(300);
            entity.Property(x => x.NormalizedName).HasMaxLength(300);
            entity.Property(x => x.BestPlayers).HasMaxLength(64);
            entity.Property(x => x.ExternalUrl).HasMaxLength(500);
            entity.Property(x => x.ThumbnailImageUrl).HasMaxLength(1000);
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<GameCopy>(entity =>
        {
            entity.ToTable("GameCopies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameId, x.OwnerParticipantId }).IsUnique();
            entity.HasIndex(x => x.GameId)
                .IsUnique()
                .HasDatabaseName("IX_GameCopies_GameId_Club")
                .HasFilter("\"OwnerParticipantId\" IS NULL AND \"Source\" = 1");

            entity.HasOne(x => x.Game)
                .WithMany(x => x.Copies)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.OwnerParticipant)
                .WithMany(x => x.GameCopies)
                .HasForeignKey(x => x.OwnerParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameInterest>(entity =>
        {
            entity.ToTable("GameInterests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ParticipantId, x.GameId }).IsUnique();

            entity.HasOne(x => x.Participant)
                .WithMany(x => x.GameInterests)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Game)
                .WithMany(x => x.Interests)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.ToTable("GameSessions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TelegramChatId, x.TelegramMessageId }).IsUnique();

            entity.HasOne(x => x.Game)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.HostParticipant)
                .WithMany(x => x.HostedGameSessions)
                .HasForeignKey(x => x.HostParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameSessionParticipant>(entity =>
        {
            entity.ToTable("GameSessionParticipants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameSessionId, x.ParticipantId }).IsUnique();

            entity.HasOne(x => x.GameSession)
                .WithMany(x => x.Participants)
                .HasForeignKey(x => x.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Participant)
                .WithMany(x => x.GameSessionParticipations)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameGathering>(entity =>
        {
            entity.ToTable("GameGatherings", table =>
                table.HasCheckConstraint(
                    "CK_GameGatherings_PlayerLimits",
                    "\"MinimumPlayers\" >= 1 AND \"MinimumPlayers\" <= \"DesiredPlayers\" AND \"DesiredPlayers\" <= \"MaximumPlayers\""));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.HasIndex(x => new { x.CommunityKey, x.StartsAtUtc });
            entity.HasIndex(x => new { x.Status, x.StartsAtUtc, x.Id });
            entity.HasIndex(x => new { x.TelegramChatId, x.TelegramMessageId }).IsUnique();

            entity.Property(x => x.CommunityKey).HasMaxLength(32);
            entity.Property(x => x.GameSnapshotJson).HasColumnType("jsonb");
            entity.Property(x => x.Description).HasMaxLength(GatheringRules.DescriptionMaxLength);
            entity.Property(x => x.CancellationReason).HasMaxLength(GatheringRules.CancellationReasonMaxLength);
            entity.Property(x => x.PublicationError).HasMaxLength(2000);

            entity.HasOne(x => x.Game)
                .WithMany(x => x.Gatherings)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Community)
                .WithMany(x => x.Gatherings)
                .HasForeignKey(x => x.CommunityKey)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OrganizerParticipant)
                .WithMany(x => x.OrganizedGatherings)
                .HasForeignKey(x => x.OrganizerParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameGatheringExpansion>(entity =>
        {
            entity.ToTable("GameGatheringExpansions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameGatheringId, x.BggId }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(300);

            entity.HasOne(x => x.GameGathering)
                .WithMany(x => x.Expansions)
                .HasForeignKey(x => x.GameGatheringId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameGatheringParticipant>(entity =>
        {
            entity.ToTable("GameGatheringParticipants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameGatheringId, x.ParticipantId }).IsUnique();
            entity.HasIndex(x => new { x.GameGatheringId, x.Status, x.JoinedAt });

            entity.HasOne(x => x.GameGathering)
                .WithMany(x => x.Participants)
                .HasForeignKey(x => x.GameGatheringId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Participant)
                .WithMany(x => x.GatheringParticipations)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CollectionImport>(entity =>
        {
            entity.ToTable("CollectionImports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new
            {
                x.Provider,
                x.ExternalUsername,
                x.ParticipantId,
                x.Target,
                x.Status
            });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasIndex(x => x.RequestedByTelegramUserId);

            entity.Property(x => x.ExternalUsername).HasMaxLength(200);
            entity.Property(x => x.ProgressJson).HasColumnType("jsonb");
            entity.Property(x => x.Error).HasMaxLength(2000);

            entity.HasOne(x => x.Participant)
                .WithMany(x => x.CollectionImports)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ParticipantConversationState>(entity =>
        {
            entity.ToTable("ParticipantConversationStates");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ParticipantId).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);

            entity.Property(x => x.State).HasMaxLength(100);
            entity.Property(x => x.DataJson).HasColumnType("jsonb");

            entity.HasOne(x => x.Participant)
                .WithOne(x => x.ConversationState)
                .HasForeignKey<ParticipantConversationState>(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
