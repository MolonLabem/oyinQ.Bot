using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Tests;

public sealed class GatheringParticipantNameTests
{
    [Fact]
    public async Task ErasedParticipantsRemainAnonymousAndCannotBeRenamedInSharedHistory()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        f.Other.DeletedAt = f.Clock.Now;
        g.Participants.Single().DisplayNameOverride = "Старое личное имя";
        Assert.Equal(ParticipantPresentation.AnonymousName, GatheringParticipantNames.GetDisplayName(g.Participants.Single()));
        var choice = GatheringPlayService.PlayerChoices(g).Single(x => x.Id == f.Other.PublicId);
        Assert.False(choice.CanRename); Assert.Equal(ParticipantPresentation.AnonymousName, choice.Name);
        Assert.Null(choice.DisplayNameOverride);
        Assert.Equal(ParticipantPresentation.AnonymousName, ParticipantPresentation.ToHtmlLink(f.Other, displayName: "Старое личное имя"));
        Assert.Throws<ArgumentException>(() => GatheringParticipantNames.Apply(g, [new(f.Other.PublicId, "Личное имя")]));
        Assert.Null(ParticipantPresentation.GetContactUrl(f.Other));
    }
    [Fact]
    public async Task NullOverrideLoadsAndUsesCurrentProfileName()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var loaded = await new GatheringPlayService(f.Db, f.Clock).Gatherings.SingleAsync();
        var member = Assert.Single(loaded.Participants);
        Assert.Null(member.DisplayNameOverride);
        Assert.Equal("Второй", GatheringParticipantNames.GetDisplayName(member));
        member.Participant.PreferredDisplayName = "Виктор";
        Assert.Equal("Виктор", GatheringParticipantNames.GetDisplayName(member));
        Assert.Equal("Виктор", GatheringPlayService.PlayerChoices(loaded).Single(x => x.Id == f.Other.PublicId).Name);
    }

    [Fact]
    public async Task OverrideSurvivesProfileRenameAndClearRestoresCurrentNameOnlyInThisGathering()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        var other = WithMember(f);
        GatheringParticipantNames.Apply(g, [new(f.Other.PublicId, "  Виктор + жена  ")]);
        f.Other.PreferredDisplayName = "Виктор (профиль)";
        Assert.Equal("Виктор + жена", GatheringParticipantNames.GetDisplayName(g.Participants.Single()));
        Assert.Equal("Виктор (профиль)", GatheringParticipantNames.GetDisplayName(other.Participants.Single()));
        Assert.Equal("Второй", f.Other.DisplayName);
        GatheringParticipantNames.Apply(g, [new(f.Other.PublicId, "   ")]);
        Assert.Null(g.Participants.Single().DisplayNameOverride);
        Assert.Equal("Виктор (профиль)", GatheringParticipantNames.GetDisplayName(g.Participants.Single()));
    }

    [Fact]
    public async Task DuplicateNamesAreAllowedButForeignIdsAndInvalidInputAreRejectedBeforeAnyNameChanges()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        GatheringParticipantNames.Apply(g, [new(f.Me.PublicId, "Виктор"), new(f.Other.PublicId, "Виктор")]);
        Assert.All(GatheringPlayService.PlayerChoices(g), x => Assert.Equal("Виктор", x.Name));
        Assert.Throws<ArgumentException>(() => GatheringParticipantNames.Apply(g,
            [new(f.Me.PublicId, "Подмена"), new(Guid.NewGuid(), "Чужой")]));
        Assert.Equal("Виктор", g.OrganizerDisplayNameOverride);
        Assert.Throws<ArgumentException>(() => GatheringParticipantNames.Normalize(new string('я', 129)));
        Assert.Throws<ArgumentException>(() => GatheringParticipantNames.Normalize("Имя\nДругая строка"));
        Assert.Throws<ArgumentException>(() => GatheringParticipantNames.Apply(g,
            [new(f.Other.PublicId, "A"), new(f.Other.PublicId, "B")]));
    }

    [Fact]
    public async Task OnlyOrganizerCanRenameUpcomingGatheringParticipants()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        await f.Db.SaveChangesAsync();
        var command = Update(g, [new(f.Other.PublicId, "Виктор (новичок)")]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Management(f).UpdateAsync(g.PublicId, "club",
            f.Other.TelegramUserId, command, default));
        Assert.Null(g.Participants.Single().DisplayNameOverride);
        await Management(f).UpdateAsync(g.PublicId, "club", f.Me.TelegramUserId, command, default);
        Assert.Equal("Виктор (новичок)", g.Participants.Single().DisplayNameOverride);
    }

    [Fact]
    public async Task RecordedPlayEditorRetainsExistingOrganizerAndAdminRightsAndExportsCurrentNames()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f);
        g.StartsAtUtc = f.Clock.Now.AddHours(-2); g.Status = GatheringStatus.Completed;
        await f.Db.SaveChangesAsync();
        var service = new GatheringPlayService(f.Db, f.Clock);
        var command = new RecordPlayCommand(true, f.Clock.Now, null,
            [new(f.Me.PublicId, 5, true), new(f.Other.PublicId, 4, false)], [], 0,
            ParticipantNames: [new(f.Other.PublicId, "Виктор + жена")]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(g.PublicId, "club", f.Other.Id, command, default));
        var play = (await service.SaveAsync(g.PublicId, "club", f.Me.Id, command, default))!;
        var member = play.Players.Single(x => x.ParticipantId == f.Other.Id);
        Assert.Equal(f.Other.PublicId, member.SourcePlayerId);
        Assert.Equal("Виктор + жена", PlayExport.From(play).Players.Single(x => x.Id == member.SourcePlayerId).Name);
        f.Other.PreferredDisplayName = "Новое имя";
        Assert.Equal("Виктор + жена", PlayExport.From(play).Players.Single(x => x.Id == member.SourcePlayerId).Name);
        await service.SaveAsync(g.PublicId, "club", f.Other.Id,
            command with { ExpectedRevision = 1, ParticipantNames = [new(f.Other.PublicId, null)] }, default, canAdminister: true);
        Assert.Equal("Новое имя", PlayExport.From(play).Players.Single(x => x.Id == member.SourcePlayerId).Name);
        f.Me.PreferredDisplayName = "Новый организатор";
        Assert.Equal("Новый организатор", PlayExport.From(play).Players.Single(x => x.Id == f.Me.PublicId).Name);
    }

    [Fact]
    public async Task OmittedNamesOnLegacyPlaySavePreserveOverridesAndStaleRevisionCannotOverwriteThem()
    {
        await using var f = new PlanningFixture();
        var g = WithMember(f); g.StartsAtUtc = f.Clock.Now.AddHours(-1); g.Status = GatheringStatus.Completed;
        GatheringParticipantNames.Apply(g, [new(f.Other.PublicId, "Виктор")]);
        await f.Db.SaveChangesAsync();
        var service = new GatheringPlayService(f.Db, f.Clock);
        var command = new RecordPlayCommand(true, f.Clock.Now, null, [new(f.Other.PublicId, null, false)], [], 0);
        await service.SaveAsync(g.PublicId, "club", f.Me.Id, command, default);
        Assert.Equal("Виктор", g.Participants.Single().DisplayNameOverride);
        await Assert.ThrowsAsync<GatheringPlayConflictException>(() => service.SaveAsync(g.PublicId, "club", f.Me.Id,
            command with { ParticipantNames = [new(f.Other.PublicId, "Подмена")] }, default));
        Assert.Equal("Виктор", g.Participants.Single().DisplayNameOverride);
    }

    [Fact]
    public async Task CreateJoinEditPublishLeaveRejoinKeepsIdentityAndEditsOriginalTelegramMessage()
    {
        await using var f = new PlanningFixture();
        var seed = f.Gathering("club", f.Clock.Now.AddHours(1));
        seed.Community.TelegramChatId = -1001;
        f.Db.GameGatherings.Remove(seed);
        f.Db.Clubs.Local.Single().CollectionJson = ClubCollectionSerializer.Serialize(new(2,
            [new(42, "Игра", null, null, 1, 4, null, [])]));
        await f.Db.SaveChangesAsync();
        var community = (await new CommunityStore(f.Db).FindByKeyAsync("club", default))!;
        var management = Management(f);
        var g = await management.CreateAsync(community, new(f.Me.TelegramUserId, null, f.Me.DisplayName),
            new("club", "catalog", 42, [], f.Clock.Now.AddHours(1), 1, 2, 4, null, false), default);
        var sender = new Sender(); var handler = new EditHandler();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(handler));
        var publication = new GatheringPublicationService(f.Db, new CommunityStore(f.Db),
            new(sender, bot, new(), NullLogger<GatheringTelegramPublisher>.Instance), f.Clock,
            NullLogger<GatheringPublicationService>.Instance);
        Assert.True(await publication.PublishAsync(g.PublicId, default));
        var participation = new GatheringService(f.Db, new(f.Db, f.Clock));
        await participation.JoinAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now, default);
        var joinedId = g.Participants.Single().Id;
        await management.UpdateAsync(g.PublicId, "club", f.Me.TelegramUserId,
            Update(g, [new(f.Other.PublicId, "Виктор <новичок>")]), default);
        Assert.True(await publication.PublishAsync(g.PublicId, default));
        using var body = JsonDocument.Parse(handler.Edits.Last());
        Assert.Equal(777, body.RootElement.GetProperty("message_id").GetInt32());
        Assert.Equal(-1001, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Contains("Виктор &lt;новичок&gt;", body.RootElement.GetProperty("caption").GetString());
        Assert.Contains($"tg://user?id={f.Other.TelegramUserId}", body.RootElement.GetProperty("caption").GetString());
        await participation.LeaveAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now, default);
        Assert.Equal(GatheringParticipationStatus.Withdrawn, g.Participants.Single().Status);
        await participation.JoinAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now, default);
        Assert.Equal(joinedId, g.Participants.Single().Id);
        Assert.Equal(f.Other.Id, g.Participants.Single().ParticipantId);
        Assert.Equal(GatheringParticipationStatus.Confirmed, g.Participants.Single().Status);
        Assert.Equal("Виктор <новичок>", g.Participants.Single().DisplayNameOverride);
        Assert.Equal("Второй", f.Other.DisplayName);
        Assert.Equal(1, sender.Sends);
    }

    private static GameGathering WithMember(PlanningFixture f)
    {
        var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        g.Participants.Add(new() { Participant = f.Other, ParticipantId = f.Other.Id,
            Status = GatheringParticipationStatus.Confirmed, JoinedAt = f.Clock.Now });
        return g;
    }

    private static UpdateGatheringCommand Update(GameGathering g, GatheringParticipantNameChange[] changes) =>
        new(g.StartsAtUtc, g.MinimumPlayers, g.DesiredPlayers, g.MaximumPlayers, g.Description, g.CanTeachRules, [], ParticipantNames: changes);

    private static GatheringManagementService Management(PlanningFixture f) => new(f.Db,
        new(f.Db, new SelectionBggClient()), new(f.Db, f.Clock), new(f.Db, new NotificationService(f.Db, f.Clock)), f.Clock);

    private sealed class Sender : ITelegramGroupMessageSender
    {
        public int Sends;
        public Task<Message> SendMessageAsync(string key, string text, ParseMode mode, ReplyMarkup? markup, CancellationToken ct)
        { Sends++; return Task.FromResult(new Message { Id = 777, Chat = new Chat { Id = -1001 } }); }
        public Task<Message> SendPhotoAsync(string key, InputFile photo, string caption, ParseMode mode, ReplyMarkup? markup, CancellationToken ct) =>
            throw new InvalidOperationException("Внешняя отправка запрещена тестом.");
    }

    private sealed class EditHandler : HttpMessageHandler
    {
        public List<string> Edits { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var result = method.ToLowerInvariant() switch
            {
                "getme" => "{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\",\"username\":\"test_bot\"}",
                "editmessagecaption" => "{\"message_id\":777,\"date\":0,\"chat\":{\"id\":-1001,\"type\":\"supergroup\"}}",
                _ => throw new InvalidOperationException("Внешняя отправка запрещена тестом.")
            };
            if (method.Equals("editMessageCaption", StringComparison.OrdinalIgnoreCase))
                Edits.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") };
        }
    }
}
