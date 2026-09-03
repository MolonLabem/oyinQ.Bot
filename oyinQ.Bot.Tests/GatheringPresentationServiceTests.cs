using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Collections;

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
        Assert.Equal("✅ Есть места", card.StatusText);
        Assert.Equal("Sardar", card.OrganizerName);
        Assert.True(card.CanTeachRules);
        Assert.True(detail.CanTeachRules);
        Assert.Equal("Стратегия", card.TypeName);
        Assert.Equal("Стратегия", detail.TypeName);
        Assert.Equal(167791, card.BggId);
        Assert.Equal("https://boardgamegeek.com/boardgame/167791", card.BggUrl);
        Assert.Equal(card.BggUrl, detail.BggUrl);
        Assert.Equal(2, detail.ConfirmedPlayers);
        Assert.Equal(["Hellas & Elysium", "Prelude"], detail.Expansions);
    }

    [Fact]
    public void MissingClassificationAndBggId_AreOmittedWithoutPlaceholder()
    {
        var gathering = CreateGathering(canTeachRules: true);
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with
        {
            BggId = null,
            Type = GameType.Other
        });
        var service = new GatheringPresentationService();

        var card = service.BuildCard(gathering, Community);
        var detail = service.BuildDetails(gathering, Community);
        var announcement = service.BuildTelegramAnnouncement(gathering, Community);

        Assert.Null(card.TypeName);
        Assert.Null(detail.TypeName);
        Assert.Null(card.BggUrl);
        Assert.Null(detail.BggUrl);
        Assert.Null(announcement.BggUrl);
        Assert.DoesNotContain("Другое", announcement.HtmlText);
    }

    [Fact]
    public void RecruitingStatusUsesConciseProductCopy()
    {
        var gathering = CreateGathering(canTeachRules: true);
        gathering.Status = GatheringStatus.Recruiting;

        Assert.Equal("🟡 Идёт набор", new GatheringPresentationService().BuildCard(gathering, Community).StatusText);
    }

    [Fact]
    public void ClubAndCampGatheringsExposeTheSameGameMetadata()
    {
        var gathering = CreateGathering(canTeachRules: true);
        var service = new GatheringPresentationService();

        var club = service.BuildCard(gathering, Community);
        var camp = service.BuildCard(gathering,
            new BotCommunity("camp", "Кэмп", -1002, BotMode.Camp, "UTC"));

        Assert.Equal(club.TypeName, camp.TypeName);
        Assert.Equal(club.BggUrl, camp.BggUrl);
    }

    [Fact]
    public void MissingImagesAreRepresentedAsNull()
    {
        var gathering = CreateGathering(canTeachRules: false);
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with
        {
            ThumbnailImageUrl = null,
            ImageUrl = null
        });
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
        Assert.Contains("🏷 Стратегия", announcement.HtmlText);
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
        GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion,
            167791,
            "Terraforming Mars",
            "https://images.example/thumb.jpg",
            "https://images.example/large.jpg",
            1,
            5,
            "4",
            [],
            "catalog",
            [],
            Type: GameType.Strategy)),
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
