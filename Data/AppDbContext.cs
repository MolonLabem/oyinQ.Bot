using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameWish> GameWishes => Set<GameWish>();
    public DbSet<RecruitmentDigest> RecruitmentDigests => Set<RecruitmentDigest>();
    public DbSet<ReleaseAnnouncement> ReleaseAnnouncements => Set<ReleaseAnnouncement>();
    public DbSet<ReleaseAnnouncementDelivery> ReleaseAnnouncementDeliveries => Set<ReleaseAnnouncementDelivery>();
    public DbSet<GatheringExternalPlayReference> GatheringExternalPlayReferences => Set<GatheringExternalPlayReference>();
    public DbSet<GatheringPlayRecord> GatheringPlayRecords => Set<GatheringPlayRecord>();
    public DbSet<GatheringPlayPlayer> GatheringPlayPlayers => Set<GatheringPlayPlayer>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<ParticipantCollectionItem> ParticipantCollectionItems => Set<ParticipantCollectionItem>();
    public DbSet<OyinQCommunity> OyinQCommunities => Set<OyinQCommunity>();
    public DbSet<ChatAdminPermission> ChatAdminPermissions => Set<ChatAdminPermission>();
    public DbSet<KnownTelegramChat> KnownTelegramChats => Set<KnownTelegramChat>();
    public DbSet<TelegramForumTopic> TelegramForumTopics => Set<TelegramForumTopic>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Camp> Camps => Set<Camp>();
    public DbSet<CampRegistration> CampRegistrations => Set<CampRegistration>();
    public DbSet<CampRegistrationDay> CampRegistrationDays => Set<CampRegistrationDay>();
    public DbSet<CampGameContribution> CampGameContributions => Set<CampGameContribution>();
    public DbSet<GameGathering> GameGatherings => Set<GameGathering>();
    public DbSet<GameGatheringExpansion> GameGatheringExpansions => Set<GameGatheringExpansion>();
    public DbSet<GameGatheringParticipant> GameGatheringParticipants => Set<GameGatheringParticipant>();
    public DbSet<GameGatheringGuest> GameGatheringGuests => Set<GameGatheringGuest>();
    public DbSet<CampBggImport> CampBggImports => Set<CampBggImport>();
    public DbSet<ClubMetadataRefresh> ClubMetadataRefreshes => Set<ClubMetadataRefresh>();
    public DbSet<ClubBggImport> ClubBggImports => Set<ClubBggImport>();
    public DbSet<PendingTelegramPeerSelection> PendingTelegramPeerSelections => Set<PendingTelegramPeerSelection>();
    public DbSet<TelegramMessageCleanup> TelegramMessageCleanups => Set<TelegramMessageCleanup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameWish>(b =>
        {
            b.HasKey(x => new { x.CommunityKey, x.ParticipantId, x.BggId });
            b.HasIndex(x => new { x.CommunityKey, x.BggId });
            b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            b.HasOne(x => x.Community).WithMany().HasForeignKey(x => x.CommunityKey).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_GameWish_BggId", "\"BggId\" > 0"));
        });
        modelBuilder.Entity<RecruitmentDigest>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.State, x.RequestedAt });
            b.HasIndex(x => x.CommunityKey).IsUnique().HasFilter("\"State\" IN (0, 1, 2)");
            b.HasOne(x => x.Community).WithMany().HasForeignKey(x => x.CommunityKey).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OyinQCommunity>().Property(x => x.RecruitmentCooldownHours).HasDefaultValue(4);
        modelBuilder.Entity<OyinQCommunity>().ToTable(t => t.HasCheckConstraint("CK_Community_RecruitmentCooldown", "\"RecruitmentCooldownHours\" BETWEEN 1 AND 24"));
        modelBuilder.Entity<ReleaseAnnouncement>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(64); b.Property(x => x.Text).HasMaxLength(3500);
            b.HasOne<Participant>().WithMany().HasForeignKey(x => x.CreatedByParticipantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReleaseAnnouncementDelivery>(b =>
        {
            b.HasKey(x => new { x.ReleaseId, x.CommunityKey });
            b.Property(x => x.Error).HasMaxLength(300);
            b.HasOne(x => x.Release).WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Community).WithMany().HasForeignKey(x => x.CommunityKey).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.State, x.AttemptedAt });
        });
        modelBuilder.Entity<GameGathering>().Property<string>("LegacyPlayOutcomeJson").HasColumnType("jsonb");
        modelBuilder.Entity<Participant>().Property(x => x.PublicId).HasDefaultValueSql("gen_random_uuid()");
        modelBuilder.Entity<GameGatheringGuest>().Property(x => x.PublicId).HasDefaultValueSql("gen_random_uuid()");
        modelBuilder.Entity<Participant>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<GameGatheringGuest>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<GatheringPlayRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.PublicId).IsUnique();
            b.HasIndex(x => x.GatheringId).IsUnique();
            b.Property(x => x.GameSnapshotJson).HasColumnType("jsonb");
            // Retained only as a legacy audit column; new links have authors and their own rows.
            b.Property<string>("ExternalUrl").HasMaxLength(2048);
            b.HasOne(x => x.Gathering).WithOne().HasForeignKey<GatheringPlayRecord>(x => x.GatheringId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.RecordedByParticipant).WithMany().HasForeignKey(x => x.RecordedByParticipantId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Players).WithOne(x => x.PlayRecord).HasForeignKey(x => x.PlayRecordId).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => t.HasCheckConstraint("CK_PlayRecord_Outcome", "(\"WasPlayed\" AND \"EndedAtUtc\" IS NOT NULL) OR (NOT \"WasPlayed\" AND \"EndedAtUtc\" IS NULL)"));
        });
        modelBuilder.Entity<GatheringExternalPlayReference>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Url).HasMaxLength(GatheringExternalPlayReference.MaxUrlLength);
            b.HasIndex(x => new { x.GatheringPlayRecordId, x.Url }).IsUnique();
            b.HasOne(x => x.PlayRecord).WithMany().HasForeignKey(x => x.GatheringPlayRecordId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AddedByParticipant).WithMany().HasForeignKey(x => x.AddedByParticipantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GatheringPlayPlayer>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.PlayRecordId, x.SourcePlayerId }).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.Property(x => x.DeduplicationKey).HasMaxLength(240);
            entity.Property(x => x.Text).HasMaxLength(4000);
            entity.Property(x => x.LastErrorCategory).HasMaxLength(80);
            entity.HasIndex(x => new { x.State, x.NextAttemptAt });
            entity.HasIndex(x => new { x.GatheringPublicId, x.ParticipantId });
            entity.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<NotificationPreferences>(entity =>
        {
            entity.HasKey(x => x.ParticipantId);
            entity.HasOne(x => x.Participant).WithOne().HasForeignKey<NotificationPreferences>(x => x.ParticipantId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("NotificationPreferences", table => table.HasCheckConstraint("CK_NotificationPreferences_Reminder", "\"ReminderLeadMinutes\" IN (0, 30, 60, 120, 360, 720, 1440)"));
        });
        modelBuilder.Entity<ParticipantCollectionItem>(entity =>
        {
            entity.ToTable("ParticipantCollectionItems", table =>
                table.HasCheckConstraint("CK_ParticipantCollectionItems_BggId", "\"BggId\" > 0"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ParticipantId, x.BggId, x.ItemType }).IsUnique();
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            entity.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OyinQCommunity>(entity =>
        {
            entity.ToTable("OyinQCommunities");
            entity.HasKey(x => x.Key);
            entity.HasIndex(x => x.TelegramChatId).IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasAlternateKey(x => new { x.Key, x.Mode });
            entity.Property(x => x.Key).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100);
        });

        modelBuilder.Entity<ChatAdminPermission>(entity =>
        {
            entity.ToTable("ChatAdminPermissions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TelegramUserId, x.CommunityKey }).IsUnique();
            entity.HasIndex(x => new { x.CommunityKey, x.RevokedAt });
            entity.Property(x => x.CommunityKey).HasMaxLength(32);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.TelegramUsername).HasMaxLength(64);
            entity.HasOne(x => x.Community)
                .WithMany(x => x.AdminPermissions)
                .HasForeignKey(x => x.CommunityKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnownTelegramChat>(entity =>
        {
            entity.ToTable("KnownTelegramChats");
            entity.HasKey(x => x.TelegramChatId);
            entity.Property(x => x.TelegramChatId).ValueGeneratedNever();
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.Username).HasMaxLength(64);
            entity.Property(x => x.TelegramPhotoFileId).HasMaxLength(256);
            entity.HasIndex(x => x.IsBotPresent);
        });

        modelBuilder.Entity<TelegramForumTopic>(entity =>
        {
            entity.ToTable("TelegramForumTopics");
            entity.HasKey(x => new { x.TelegramChatId, x.MessageThreadId });
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.HasIndex(x => new { x.TelegramChatId, x.IsDeleted, x.IsClosed });
            entity.HasOne<KnownTelegramChat>()
                .WithMany()
                .HasForeignKey(x => x.TelegramChatId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Club>(entity =>
        {
            entity.ToTable("Clubs", table =>
                table.HasCheckConstraint("CK_Clubs_BotChatMode", "\"BotChatMode\" = 0"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BotChatKey).IsUnique();
            entity.Property(x => x.BotChatKey).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(160);
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
                    "CK_Camps_OperatingWindow",
                    "(\"StartsAtUtc\" IS NULL AND \"EndsAtUtc\" IS NULL) OR (\"StartsAtUtc\" IS NOT NULL AND \"EndsAtUtc\" IS NOT NULL AND \"StartsAtUtc\" < \"EndsAtUtc\")");
            });
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BotChatKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.EndsAtUtc, x.Id });
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
            entity.HasIndex(x => x.ParticipantId).IsUnique()
                .HasDatabaseName("IX_CampBggImports_ActiveProfileParticipant")
                .HasFilter("\"CampId\" IS NULL AND \"Status\" IN (0, 1)");
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

        modelBuilder.Entity<GameGatheringGuest>(entity =>
        {
            entity.ToTable("GameGatheringGuests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameGatheringId, x.Id });
            entity.Property(x => x.DisplayName).HasMaxLength(GatheringRules.GuestDisplayNameMaxLength);
            entity.HasOne(x => x.GameGathering)
                .WithMany(x => x.Guests)
                .HasForeignKey(x => x.GameGatheringId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CreatedByParticipant)
                .WithMany(x => x.CreatedGatheringGuests)
                .HasForeignKey(x => x.CreatedByParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

    }
}
