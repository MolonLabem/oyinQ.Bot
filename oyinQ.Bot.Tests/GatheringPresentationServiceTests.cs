using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringPresentationServiceTests
{
    private static readonly BotCommunity Community =
        new("club", "Клуб", -1001, BotMode.Club, "UTC");

    [Fact]
    public void CardUsesThumbnailAndDetailUsesLargeImage()
    {
        var gathering = CreateGathering(canTeachRules: true);
        var service = new GatheringPresentationService();

        var card = service.BuildCard(gathering, Community);
        var detail = service.BuildDetails(gathering, Community);

        Assert.Equal("https://images.example/thumb.jpg", card.ImageUrl);
        Assert.Equal("https://images.example/large.jpg", detail.ImageUrl);
        Assert.Equal("Могу объяснить правила", card.RulesText);
        Assert.Equal("Sardar", card.OrganizerName);
        Assert.True(card.CanTeachRules);
        Assert.True(detail.CanTeachRules);
        Assert.Equal(2, detail.ConfirmedPlayers);
        Assert.Equal(["Hellas & Elysium", "Prelude"], detail.Expansions);
    }

    [Fact]
    public void MissingImagesAreRepresentedAsNull()
    {
        var gathering = CreateGathering(canTeachRules: false);
        gathering.Game!.ThumbnailImageUrl = null;
        gathering.Game.ImageUrl = null;
        var service = new GatheringPresentationService();

        var card = service.BuildCard(gathering, Community);
        var detail = service.BuildDetails(gathering, Community);

        Assert.Null(card.ImageUrl);
        Assert.Null(detail.ImageUrl);
        Assert.Equal("Опыт с игрой желателен", detail.RulesText);
    }

    [Fact]
    public void CardTruncatesDescriptionAndTelegramIncludesConcisePresentation()
    {
        var gathering = CreateGathering(canTeachRules: true);
        gathering.Description = new string('а', 150);
        var service = new GatheringPresentationService();

        var card = service.BuildCard(gathering, Community);
        var announcement = service.BuildTelegramAnnouncement(gathering, Community);

        Assert.Equal(120, card.Description?.Length);
        Assert.EndsWith("…", card.Description);
        Assert.Contains("📖 Правила объясню", announcement.HtmlText);
        Assert.Contains("Дополнения:", announcement.HtmlText);
        Assert.Equal("https://images.example/large.jpg", announcement.ImageUrl);
    }

    private static GameGathering CreateGathering(bool canTeachRules) => new()
    {
        PublicId = Guid.NewGuid(),
        StartsAtUtc = new DateTimeOffset(2026, 9, 5, 19, 0, 0, TimeSpan.Zero),
        MinimumPlayers = 3,
        DesiredPlayers = 4,
        MaximumPlayers = 5,
        Description = "Новичкам тоже можно.",
        CanTeachRules = canTeachRules,
        Status = GatheringStatus.Ready,
        Game = new Game
        {
            Name = "Terraforming Mars",
            ThumbnailImageUrl = "https://images.example/thumb.jpg",
            ImageUrl = "https://images.example/large.jpg"
        },
        OrganizerParticipant = new Participant { DisplayName = "Sardar" },
        Participants =
        [
            new GameGatheringParticipant { ParticipantId = 2, Status = GatheringParticipationStatus.Confirmed },
            new GameGatheringParticipant { ParticipantId = 3, Status = GatheringParticipationStatus.Waitlisted }
        ],
        Expansions =
        [
            new GameGatheringExpansion { BggId = 2, Name = "Prelude" },
            new GameGatheringExpansion { BggId = 3, Name = "Hellas & Elysium" }
        ]
    };
}
