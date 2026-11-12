using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;

namespace oyinQ.Bot.Tests;

public sealed class GatheringPresentationServiceTests
{
    [Fact]
    public void PersonalParticipationUsesTheSameQueueAcrossListDetailsAndProfile()
    {
        var g = CreateGathering(true); g.OrganizerParticipantId = 1;
        g.Participants = [new() { Id = 4, ParticipantId = 3, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = DateTimeOffset.UnixEpoch },
            new() { Id = 2, ParticipantId = 2, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = DateTimeOffset.UnixEpoch },
            new() { Id = 3, ParticipantId = 4, Status = GatheringParticipationStatus.Confirmed }];
        Assert.Equal(new GatheringParticipationPresentation("Organizer", null), GatheringPresentationService.BuildParticipation(g, 1));
        Assert.Equal(new GatheringParticipationPresentation("Waitlisted", 2), GatheringPresentationService.BuildParticipation(g, 3));
        Assert.Equal(2, new GatheringPresentationService().BuildProfileSchedule(g, Community, 3).WaitlistPosition);
        Assert.Equal(new GatheringParticipationPresentation("Confirmed", null), GatheringPresentationService.BuildParticipation(g, 4));
        Assert.Equal("None", GatheringPresentationService.BuildParticipation(g, 99).Status);
        g.Participants.First(x => x.ParticipantId == 2).Status = GatheringParticipationStatus.Withdrawn;
        Assert.Equal(1, GatheringPresentationService.BuildParticipation(g, 3).WaitlistPosition);
    }

    [Fact]
    public void DurationAndBoxUncertaintyReachCardsDetailsAndAnnouncementWithoutOwnerDisclosure()
    {
        var g = CreateGathering(true);
        var game = GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson);
        g.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(game with { MinPlayTimeMinutes = 90, MaxPlayTimeMinutes = 120 });
        var community = Community with { Mode = BotMode.Camp };
        var service = new GatheringPresentationService();
        var missing = GameProviderService.Describe(false, []);
        Assert.Equal("Коробка пока не подтверждена", service.BuildCard(g, community, missing).BoxWarning);
        Assert.Null(service.BuildCard(g, Community, missing).BoxWarning);
        Assert.Null(service.BuildCard(g, community, GameProviderService.Describe(true, [])).BoxWarning);
        Assert.Equal(90, service.BuildCard(g, community, missing).MinPlayTimeMinutes);
        Assert.Equal(120, service.BuildDetails(g, community).MaxPlayTimeMinutes);
        var html = service.BuildTelegramAnnouncement(g, community, missing).HtmlText;
        Assert.Contains("90–120 мин", html);
        Assert.True(html.IndexOf(missing.Warning!, StringComparison.Ordinal) < html.IndexOf("Собирает", StringComparison.Ordinal));
        g.Status = GatheringStatus.Cancelled;
        Assert.Null(service.BuildCard(g, community, missing).BoxWarning);
        Assert.DoesNotContain(missing.Warning!, service.BuildTelegramAnnouncement(g, community, missing).HtmlText);
    }

    [Theory]
    [InlineData(GatheringStatus.Cancelled, "Сбор отменён")]
    [InlineData(GatheringStatus.Closed, "Запись закрыта")]
    [InlineData(GatheringStatus.Completed, "Запись завершена")]
    public void ClosedOrPastAnnouncementsLeadWithStateAndNeverInviteMorePlayers(GatheringStatus status, string label)
    {
        var g = CreateGathering(true); g.Status = status; g.CancellationReason = "Причина <&>";
        var service = new GatheringPresentationService();
        var html = service.BuildTelegramAnnouncement(g, Community).HtmlText;
        Assert.True(html.IndexOf(label, StringComparison.Ordinal) < html.IndexOf("📅", StringComparison.Ordinal));
        Assert.Null(service.BuildCard(g, Community).Recruitment); Assert.Null(service.BuildDetails(g, Community).Recruitment);
        Assert.DoesNotContain("ждём", html); Assert.DoesNotContain("для начала", html);
        Assert.Contains("Занято мест: 2 из 5", html);
        if (status == GatheringStatus.Cancelled) Assert.Contains("Причина &lt;&amp;&gt;", html);
    }

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
        Assert.Equal("Правила объясню", card.RulesText);
        Assert.Equal("✅ Есть места", card.StatusText);
        Assert.Equal("Sardar", card.OrganizerName);
        Assert.True(card.CanTeachRules);
        Assert.True(detail.CanTeachRules);
        Assert.Equal("Стратегия", card.TypeName);
        Assert.Equal("Стратегия", detail.TypeName);
        Assert.Equal(["Стратегия"], detail.TypeNames);
        Assert.Equal(["Научная фантастика"], detail.CategoryNames);
        Assert.Equal(["Совместная игра"], detail.MechanicNames);
        Assert.Equal(167791, card.BggId);
        Assert.Equal("https://boardgamegeek.com/boardgame/167791", card.BggUrl);
        Assert.Equal(card.BggUrl, detail.BggUrl);
        Assert.Equal(2, detail.OccupiedSeats);
        Assert.Equal(["Hellas & Elysium", "Prelude"], detail.Expansions);
        Assert.Equal("сб, 5 сентября, 19:00", card.LocalDateTime);
        Assert.Equal(card.LocalDateTime, detail.LocalDateTime);
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
        Assert.Contains("⏳ Лист ожидания: 1", announcement.HtmlText);
        Assert.Contains("📅 сб, 5 сентября, 19:00", announcement.HtmlText);
        Assert.Equal("https://images.example/large.jpg", announcement.ImageUrl);
    }

    [Fact]
    public void ComplexityUsesSnapshotAndSamePresentationInCardsDetailsAndTelegram()
    {
        var gathering = CreateGathering(true);
        var service = new GatheringPresentationService();
        Assert.Null(service.BuildCard(gathering, Community).ComplexityInfo);
        Assert.DoesNotContain("⚖️", service.BuildTelegramAnnouncement(gathering, Community).HtmlText);
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with
        { ComplexityWeight = 2.1234m, Complexity = GameComplexity.MediumHeavy });
        var card = service.BuildCard(gathering, Community);
        Assert.Equal("Сложная", card.ComplexityInfo!.DisplayName);
        Assert.Equal(2.1234m, card.ComplexityInfo.Weight);
        Assert.Equal(card.ComplexityInfo, service.BuildDetails(gathering, Community).ComplexityInfo);
        Assert.Contains("⚖️ Сложная", service.BuildTelegramAnnouncement(gathering, Community).HtmlText);
        gathering.Description = new string('x', 220);
        for (var index = 0; index < 30; index++)
            gathering.Guests.Add(new GameGatheringGuest { DisplayName = new string('я', 100) });
        var html = service.BuildTelegramAnnouncement(gathering, Community).HtmlText;
        Assert.Contains("⚖️ Сложная", html);
        var visible = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));
        Assert.True(visible.Length <= 1024);
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
            Type: GameType.Strategy,
            Categories: [new GameTaxonomyItem(1016, "Science Fiction")],
            Mechanics: [new GameTaxonomyItem(2023, "Cooperative Game")])),
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
