using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameCopy> GameCopies => Set<GameCopy>();
    public DbSet<GameInterest> GameInterests => Set<GameInterest>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameSessionParticipant> GameSessionParticipants => Set<GameSessionParticipant>();
    public DbSet<CollectionImport> CollectionImports => Set<CollectionImport>();
    public DbSet<ParticipantConversationState> ParticipantConversationStates => Set<ParticipantConversationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Participant>(entity =>
        {
            entity.ToTable("Participants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TelegramUserId).IsUnique();
            entity.Property(x => x.TelegramUsername).HasMaxLength(64);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.PreferredDisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("Games");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BggId).IsUnique();
            entity.HasIndex(x => x.TeseraAlias).IsUnique();
            entity.HasIndex(x => x.NormalizedName);

            entity.Property(x => x.TeseraAlias).HasMaxLength(200);
            entity.Property(x => x.Name).HasMaxLength(300);
            entity.Property(x => x.NormalizedName).HasMaxLength(300);
            entity.Property(x => x.BestPlayers).HasMaxLength(64);
            entity.Property(x => x.ExternalUrl).HasMaxLength(500);
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
