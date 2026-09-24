using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace oyinQ.Bot.Tests;

public sealed class CampParticipantExportTests
{
    [Fact]
    public async Task AuthenticatedRoutesShareFiltersProtectDownloadsAndIgnoreDestinationPayloads()
    {
        await using var f = await Fixture.Create();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(f.Db); builder.Services.AddSingleton(f.Service);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new BotOptions { Token = "123:test" }));
        builder.Services.AddScoped<TelegramMiniAppAuthenticator>(); builder.Services.AddScoped<ParticipantIdentityService>();
        await using var app = builder.Build();
        var routes = app.MapGroup("/api/miniapp/admin");
        routes.AddEndpointFilter<MiniAppIdentityFilter>(); routes.MapCampParticipantEndpoints();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            const string path = "/api/miniapp/admin/camps/1/participants";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "/export/xlsx")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Telegram-Init-Data", SignedData(42));
            var all = await client.GetFromJsonAsync<CampAdminParticipants>(path);
            Assert.Equal(3, all!.Participants.Count);
            var filtered = await client.GetFromJsonAsync<CampAdminParticipants>(path + "?accommodation=unanswered");
            Assert.Single(filtered!.Participants);
            var download = await client.GetAsync(path + "/export/csv?scope=filtered&accommodation=unanswered&page=1");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl!.NoStore);
            Assert.Equal("text/csv", download.Content.Headers.ContentType!.MediaType);
            Assert.EndsWith(".csv", download.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal(2, ReadCsv(await download.Content.ReadAsByteArrayAsync()).Count);
            var unfiltered = await client.GetAsync(path + "/export/csv?scope=all&accommodation=unanswered");
            Assert.Equal(4, ReadCsv(await unfiltered.Content.ReadAsByteArrayAsync()).Count);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "/export/csv?scope=page")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?attendanceDate=wrong")).StatusCode);
            var sent = await client.PostAsJsonAsync(path + "/export/csv/send-to-me?scope=filtered&accommodation=unanswered&chatId=-100001",
                new { chatId = -100001, telegramUserId = 999 });
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            Assert.Equal("42", Assert.Single(f.Transport.Calls).Fields["chat_id"]);
            client.DefaultRequestHeaders.Remove("X-Telegram-Init-Data");
            client.DefaultRequestHeaders.Add("X-Telegram-Init-Data", SignedData(78));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "/export/xlsx")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path + "/send-to-me", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path + "/export/csv/send-to-me", null)).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private static string SignedData(long actor)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["user"] = JsonSerializer.Serialize(new { id = actor, first_name = "Администратор" }) };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexString(hash);
    }

    [Fact]
    public async Task SharedRosterKeepsExactDaysNullableHousingAndStableOrderingInEveryFormat()
    {
        await using var f = await Fixture.Create();
        var roster = await f.Service.GetAsync(42, 1, default);
        Assert.Equal(3, roster.TotalCount);
        Assert.Equal(["Аня", "Борис", "Имя не указано"], roster.Participants.Select(x => x.DisplayName));
        var anna = roster.Participants[0];
        Assert.Equal(2, anna.DayCount);
        Assert.Equal("01.09.2026, 03.09.2026", anna.DatesText);
        Assert.Null(anna.NeedsAccommodation);
        Assert.Equal("Не указано", anna.AccommodationText);
        Assert.Equal("anna", anna.TelegramUsername);
        Assert.Equal("https://t.me/anna?profile", anna.ContactUrl);
        Assert.Equal("Не нужно", roster.Participants[1].AccommodationText);
        Assert.Equal(0, roster.Participants[2].DayCount);
        Assert.Equal("Не указаны", roster.Participants[2].DatesText);

        var csv = await f.Service.ExportAsync(42, 1, "csv", default);
        var csvRows = ReadCsv(csv.Content);
        Assert.Equal(CampParticipantExport.Headers, csvRows[0]);
        using var workbook = new XLWorkbook(new MemoryStream((await f.Service.ExportAsync(42, 1, "xlsx", default)).Content));
        var sheet = workbook.Worksheet(1);
        var textMessages = CampParticipantMessages.Build(roster).Select(message => message.Text).ToArray();
        for (var i = 0; i < roster.Participants.Count; i++)
        {
            var person = roster.Participants[i];
            Assert.Equal(person.DisplayName, csvRows[i + 1][1]);
            Assert.Equal(person.DatesText, csvRows[i + 1][4]);
            Assert.Equal(person.DayCount.ToString(), csvRows[i + 1][5]);
            Assert.Equal(person.AccommodationText, csvRows[i + 1][6]);
            Assert.Equal(person.DisplayName, sheet.Cell(i + 7, 2).GetString());
            Assert.Equal(person.DatesText, sheet.Cell(i + 7, 5).GetString());
            Assert.Equal(person.DayCount, sheet.Cell(i + 7, 6).GetValue<int>());
            Assert.Equal(person.AccommodationText, sheet.Cell(i + 7, 7).GetString());
            Assert.Contains(textMessages, message => message.Contains($"{i + 1}. {person.DisplayName}") && message.Contains(person.DatesText));
        }
        Assert.DoesNotContain("999001", Encoding.UTF8.GetString(csv.Content));
        Assert.DoesNotContain("tg://user", Encoding.UTF8.GetString(csv.Content));
    }

    [Fact]
    public async Task FiltersSelectEveryMatchAndExportsNeverUseAPageOrInventAttendance()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) f.AddRegistration(1000 + i, $"Игрок {i:000}", false, "Астана");
        await f.Db.SaveChangesAsync();
        var filter = new CampParticipantFilter("АСТАНА", new(2026, 9, 3), "not-needed");
        var roster = await f.Service.GetAsync(42, 1, default, filter);
        Assert.Equal(123, roster.TotalCount);
        Assert.Equal(121, roster.Participants.Count);
        Assert.Equal(122, ReadCsv((await f.Service.ExportAsync(42, 1, "csv", default, filter)).Content).Count);
        using var workbook = new XLWorkbook(new MemoryStream((await f.Service.ExportAsync(42, 1, "xlsx", default, filter)).Content));
        Assert.Equal(127, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
        Assert.Contains("АСТАНА", workbook.Worksheet(1).Cell(3, 1).GetString());
        Assert.Equal(124, ReadCsv((await f.Service.ExportAsync(42, 1, "csv", default)).Content).Count);
        Assert.Single((await f.Service.GetAsync(42, 1, default, new("@ANNA"))).Participants);
        Assert.Single((await f.Service.GetAsync(42, 1, default, new(Accommodation: "unanswered"))).Participants);
        Assert.Empty((await f.Service.GetAsync(42, 1, default, new(AttendanceDate: new(2026, 9, 2)))).Participants);
        Assert.Throws<ArgumentException>(() => new CampParticipantFilter(Accommodation: "maybe").Normalize());
    }

    [Fact]
    public async Task EveryReadDownloadAndSendRechecksScopedPermissionAndDeletedBindings()
    {
        await using var f = await Fixture.Create();
        await AssertDenied(77); // Telegram admin alone.
        await AssertDenied(78); // Ordinary participant.
        f.Db.Clubs.Add(new() { Id = 3, Name = "Клуб-источник", BotChatKey = "source-club", BotChat = new()
            { Key = "source-club", Name = "Клуб-источник", Mode = BotMode.Club, TimeZoneId = "UTC", TelegramChatId = -10003 } });
        f.Camp.SourceClubId = 3;
        f.Db.ChatAdminPermissions.Add(new() { CommunityKey = "source-club", TelegramUserId = 77, GrantedByTelegramUserId = 42 });
        await f.Db.SaveChangesAsync();
        await AssertDenied(77); // Source-club permission does not authorize its Camp.
        f.Db.ChatAdminPermissions.Add(new() { CommunityKey = "camp-a", TelegramUserId = 77, GrantedByTelegramUserId = 42 });
        await f.Db.SaveChangesAsync();
        Assert.Equal(3, (await f.Service.GetAsync(77, 1, default)).Participants.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ExportAsync(77, 2, "csv", default));
        f.Verifier.IsAdmin = false;
        await AssertDenied(77);
        // Global Super Admin does not need group membership or live Telegram admin status.
        Assert.Single((await f.Service.GetAsync(42, 2, default)).Participants);
        f.Verifier.IsAdmin = true;
        (await f.Db.ChatAdminPermissions.SingleAsync(x => x.CommunityKey == "camp-a")).RevokedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
        await AssertDenied(77);
        f.Camp.Status = CampStatus.Closed;
        Assert.Equal(3, (await f.Service.GetAsync(42, 1, default)).Participants.Count);
        f.Camp.BotChat.DeletedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
        await AssertDenied(42);
        Assert.Empty(f.Transport.Calls);

        async Task AssertDenied(long actor)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(actor, 1, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ExportAsync(actor, 1, "xlsx", default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SendToActorAsync(actor, 1, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SendFileToActorAsync(actor, 1, "csv", default));
        }
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +SUM(A1:A2)")]
    [InlineData("\t\r\n-123")]
    [InlineData("\0\u001f@evil")]
    [InlineData("\uFEFF\u200B=HYPERLINK(\"https://example.test\")")]
    public void CsvNeutralizesFormulaPrefixesAndExcelStoresText(string text)
    {
        var roster = Roster([new(1, text, "Город, \"цитата\"\nещё 🏕️", [], null, "example", null)]);
        var csv = CampParticipantExport.Create(roster, "csv", Now, default);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csv.Content.Take(3));
        var row = ReadCsv(csv.Content)[1];
        Assert.Equal("'" + text, row[1]);
        Assert.Equal("'@example", row[2]);
        Assert.Equal("Город, \"цитата\"\nещё 🏕️", row[3]);
        using var workbook = new XLWorkbook(new MemoryStream(CampParticipantExport.Create(roster, "xlsx", Now, default).Content));
        var cell = workbook.Worksheet(1).Cell(7, 2);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.Equal(text.ReplaceLineEndings("\n"), cell.GetString());
        Assert.Equal(XLDataType.Number, workbook.Worksheet(1).Cell(7, 1).DataType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealExcelHasOneSheetFrozenHeaderMetadataAndValidOpenXml(bool empty)
    {
        var roster = Roster(empty ? [] : [new(1, "Анна 🏕️", null, [new(2026, 9, 1)], true, null, null)]);
        var result = CampParticipantExport.Create(roster, "xlsx", Now, default);
        Assert.Equal(CampParticipantExport.ExcelContentType, result.ContentType);
        Assert.Equal("Участники-Кэмп-2026-09-19.xlsx", result.FileName);
        using var stream = new MemoryStream(result.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        using var zip = new ZipArchive(new MemoryStream(result.Content));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("externalLinks") || entry.FullName.Contains("vbaProject"));
        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Contains("19.09.2026 04:30", sheet.Cell(2, 1).GetString());
        Assert.Equal(6, sheet.SheetView.SplitRow);
        Assert.Equal(empty ? 6 : 7, sheet.LastRowUsed()!.RowNumber());
        if (!empty) Assert.True(Assert.Single(sheet.Tables).ShowAutoFilter);
    }

    [Theory]
    [InlineData("'Анна")]
    [InlineData("Имя; с, запятой и \"кавычками\" 🏕️")]
    public void ExcelPreservesLiteralTextWithoutFormulas(string name)
    {
        using var workbook = new XLWorkbook(new MemoryStream(CampParticipantExport.Create(
            Roster([new(1, name, null, [], null, null, null)]), "xlsx", Now, default).Content));
        Assert.Equal(name, workbook.Worksheet(1).Cell(7, 2).GetString());
        Assert.False(workbook.Worksheet(1).Cell(7, 2).HasFormula);
    }

    [Fact]
    public async Task TechnicalCsvKeepsItsExistingHeadersOrderAndUnsanitizedValues()
    {
        await using var f = await Fixture.Create();
        var files = await new CsvExportService(f.Db, f.Authorization).CreateAllAsync(42, "camp-a", default);
        try
        {
            var csv = ReadCsv(files.Single(x => x.FileName == "camp-registrations.csv").Content.ToArray());
            Assert.Equal(CsvExportService.CampRegistrationHeaders, csv[0]);
            Assert.Equal("999001", csv[1][4]);
            Assert.Equal("99", csv[1][7]);
            Assert.Equal("2026-09-01;2026-09-03", csv[1][8]);
            Assert.Equal("", csv[1][9]);
        }
        finally { foreach (var file in files) file.Content.Dispose(); }
    }

    [Fact]
    public async Task RichTablesUseTypedEscapedRowsAndCurrentCampLink()
    {
        await using var f = await Fixture.Create();
        var registration = await f.Db.CampRegistrations.FirstAsync();
        registration.DisplayName = "<b>Аня & 🏕️</b>";
        await f.Db.SaveChangesAsync();
        var result = await f.Service.SendToActorAsync(42, 1, default);
        var call = Assert.Single(f.Transport.Calls);
        Assert.Equal("sendRichMessage", call.Method);
        using var json = JsonDocument.Parse(call.Body);
        Assert.Equal(42, json.RootElement.GetProperty("chat_id").GetInt64());
        var blocks = json.RootElement.GetProperty("rich_message").GetProperty("blocks");
        Assert.Equal("table", blocks[1].GetProperty("type").GetString());
        var rows = blocks[1].GetProperty("cells");
        Assert.Equal(4, rows.GetArrayLength());
        Assert.Contains("<b>Аня & 🏕️</b>", rows[1][0].GetProperty("text").GetString());
        Assert.Contains("01.09.2026, 03.09.2026", rows[1][1].GetProperty("text").GetString());
        Assert.Equal("Не указано", rows[1][2].GetProperty("text").GetString());
        var button = json.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0];
        Assert.Equal("https://example.test/app/?admin=1&adminCommunity=camp-a&adminSection=participants", button.GetProperty("web_app").GetProperty("url").GetString());
        Assert.Equal(3, result.ParticipantCount);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task PrivateAttachmentsReuseTheExportAndHaveNoClientChosenDestination(string format)
    {
        await using var f = await Fixture.Create();
        var filter = new CampParticipantFilter(Accommodation: "unanswered");
        var result = await f.Service.SendFileToActorAsync(42, 1, format, default, filter);
        var call = Assert.Single(f.Transport.Calls);
        Assert.Equal("sendDocument", call.Method);
        Assert.Contains("42", call.Fields["chat_id"]);
        Assert.Contains("." + format, call.FileName);
        if (format == "csv") Assert.Equal((await f.Service.ExportAsync(42, 1, format, default, filter)).Content, call.FileBytes);
        else
        {
            using var workbook = new XLWorkbook(new MemoryStream(call.FileBytes!));
            Assert.Equal("Аня", workbook.Worksheet(1).Cell(7, 2).GetString());
            Assert.Equal(7, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
        }
        Assert.Equal(1, result.ParticipantCount);
    }

    [Fact]
    public async Task UnsupportedFormatFallsBackOnlyForRejectedChunksWithoutReplayingEarlierRows()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 80; i++) f.AddRegistration(1000 + i, $"Игрок {i:000}", true, "Алматы");
        await f.Db.SaveChangesAsync();
        var chunks = CampParticipantMessages.Build(await f.Service.GetAsync(42, 1, default));
        Assert.True(chunks.Count > 2);
        f.Transport.Failure = index => index == 2 ? new ApiRequestException("Bad Request: RICH_MESSAGES_NOT_SUPPORTED", 400) : null;
        var result = await f.Service.SendToActorAsync(42, 1, default);
        Assert.Equal(chunks.Count, result.MessageCount);
        Assert.Equal(chunks.Count + 1, f.Transport.Calls.Count);
        Assert.Equal("sendRichMessage", f.Transport.Calls[0].Method);
        Assert.Equal("sendRichMessage", f.Transport.Calls[1].Method);
        Assert.All(f.Transport.Calls.Skip(2), call => Assert.Equal("sendMessage", call.Method));
        using var fallback = JsonDocument.Parse(f.Transport.Calls[2].Body);
        Assert.Equal(chunks[1].Text, fallback.RootElement.GetProperty("text").GetString());
        Assert.Equal(83, chunks.Sum(x => x.ParticipantCount));
        Assert.All(chunks, chunk => Assert.InRange(Encoding.UTF8.GetByteCount(chunk.Text), 1, 3900));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousAndDefinitivePartialFailuresNeverRetryTheRoster(bool definitive)
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 80; i++) f.AddRegistration(1000 + i, $"Игрок {i:000}", false, "Алматы");
        await f.Db.SaveChangesAsync();
        f.Transport.Failure = index => index == 2 ? definitive ? new ApiRequestException("Forbidden: bot was blocked", 403) : new HttpRequestException("timeout") : null;
        var error = await Assert.ThrowsAsync<CampParticipantDeliveryException>(() => f.Service.SendToActorAsync(42, 1, default));
        Assert.Equal(definitive ? "camp_participant_delivery_partial" : "camp_participant_delivery_unknown", error.Code);
        Assert.Contains("часть списка", error.Message);
        Assert.Equal(2, f.Transport.Calls.Count);
    }

    [Fact]
    public async Task EmptyRosterPrivateChatFailureAndCancellationAreExplicit()
    {
        await using var f = await Fixture.Create();
        var noMatch = new CampParticipantFilter("Нет такого имени");
        Assert.Equal(0, (await f.Service.SendToActorAsync(42, 1, default, noMatch)).ParticipantCount);
        f.Transport.Failure = _ => new ApiRequestException("Bad Request: chat not found", 400);
        var failure = await Assert.ThrowsAsync<CampParticipantDeliveryException>(() => f.Service.SendFileToActorAsync(42, 1, "csv", default));
        Assert.Equal("private_chat_required", failure.Code);
        Assert.Contains("Старт", failure.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ExportAsync(42, 1, "csv", cancellation.Token));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 19, 30, 0, TimeSpan.Zero);
    private static CampAdminParticipants Roster(CampAdminParticipant[] participants) =>
        new(1, "../Кэмп<>:\"/", participants) { TimeZoneId = "Asia/Tokyo" };

    private static List<string[]> ReadCsv(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var rows = new List<string[]>(); var row = new List<string>(); var value = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; }
            else if (!quoted && c == ',') { row.Add(value.ToString()); value.Clear(); }
            else if (!quoted && c == '\n') { row.Add(value.ToString().TrimEnd('\r')); rows.Add(row.ToArray()); row.Clear(); value.Clear(); }
            else value.Append(c);
        }
        Assert.False(quoted);
        return rows;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Verifier Verifier { get; } = new();
        public Transport Transport { get; } = new();
        public Camp Camp { get; private set; } = null!;
        public AdminAuthorizationService Authorization { get; private set; } = null!;
        public CampParticipantAdminService Service { get; private set; } = null!;
        private HttpClient http = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Camp = NewCamp(1, "camp-a");
            f.Db.Camps.AddRange(f.Camp, NewCamp(2, "camp-b"));
            f.AddRegistration(1, "Аня", null, "Алматы", " @anna ");
            f.AddRegistration(2, "Борис", false, "Астана");
            f.AddRegistration(3, null, true, null).SelectedDays = [];
            f.AddRegistration(4, "Чужой", true, "Алматы", campId: 2);
            await f.Db.SaveChangesAsync();
            f.Authorization = new(f.Db, f.Verifier, Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { 42 } }), TimeProvider.System);
            f.http = new HttpClient(f.Transport);
            f.Service = new(f.Db, f.Authorization, new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", f.http),
                NullLogger<CampParticipantAdminService>.Instance, new MiniAppLinkBuilder(Options.Create(new BotOptions { PublicBaseUrl = "https://example.test" })), new FixedTime());
            return f;
        }
        public CampRegistration AddRegistration(int id, string? name, bool? housing, string? city, string? username = null, long campId = 1)
        {
            var value = new CampRegistration { Id = id, CampId = campId, Participant = new() { Id = id, TelegramUserId = 999000 + id, DisplayName = name ?? "", TelegramUsername = username },
                DisplayName = name, City = city, NeedsAccommodation = housing, DaysStaying = 99,
                SelectedDays = [new() { Date = new(2026, 9, 3) }, new() { Date = new(2026, 9, 1) }] };
            Db.CampRegistrations.Add(value); return value;
        }
        private static Camp NewCamp(long id, string key) => new() { Id = id, Name = key, BotChatKey = key, Status = CampStatus.Active,
            BotChat = new() { Key = key, Name = key, TelegramChatId = -10000 - id, TimeZoneId = "Asia/Almaty", Mode = BotMode.Camp, IsActive = true } };
        public async ValueTask DisposeAsync() { http.Dispose(); await Db.DisposeAsync(); }
    }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Verifier : ITelegramChatAdministratorVerifier
    {
        public bool IsAdmin { get; set; } = true;
        public Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct) => Task.FromResult(IsAdmin && userId == 77);
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long chatId, CancellationToken ct) => Task.FromResult<IReadOnlyList<EligibleGroupAdministrator>>([]);
    }
    private sealed record Call(string Method, string Body, Dictionary<string, string> Fields, byte[]? FileBytes, string? FileName);
    private sealed class Transport : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public Func<int, Exception?>? Failure { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var fields = new Dictionary<string, string>(); byte[]? file = null; string? fileName = null;
            if (request.Content is MultipartFormDataContent multipart)
                foreach (var part in multipart)
                {
                    var disposition = part.Headers.ContentDisposition!;
                    if (disposition.FileName is not null) { file = await part.ReadAsByteArrayAsync(ct); fileName = disposition.FileName; }
                    else fields[disposition.Name!.Trim('"')] = await part.ReadAsStringAsync(ct);
                }
            Calls.Add(new(request.RequestUri!.Segments.Last(), request.Content is MultipartFormDataContent ? "" : await request.Content!.ReadAsStringAsync(ct), fields, file, fileName));
            if (Failure?.Invoke(Calls.Count) is { } failure)
            {
                if (failure is ApiRequestException api) return new((HttpStatusCode)api.ErrorCode)
                { Content = new StringContent(JsonSerializer.Serialize(new { ok = false, error_code = api.ErrorCode, description = api.Message }), Encoding.UTF8, "application/json") };
                throw failure;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1,\"date\":1,\"chat\":{\"id\":42,\"type\":\"private\"}}}", Encoding.UTF8, "application/json") };
        }
    }
}
