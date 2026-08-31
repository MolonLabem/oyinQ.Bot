using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Admin;

public sealed record CsvExportFile(string FileName, MemoryStream Content);

public sealed class CsvExportService(
    AppDbContext dbContext,
    IAdministratorStore administratorStore)
{
    public static readonly string[] CampRegistrationHeaders =
        ["id", "camp_id", "camp_name", "participant_id", "telegram_user_id", "display_name", "city",
            "days_staying", "selected_dates", "needs_accommodation", "created_at", "updated_at"];
    public static readonly string[] CampContributionHeaders =
        ["id", "camp_id", "camp_name", "participant_id", "telegram_user_id", "bgg_id", "item_type",
            "source", "commitment", "parent_bgg_id", "parent_bgg_ids", "created_at", "updated_at"];
    public async Task<IReadOnlyList<CsvExportFile>> CreateAllAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(telegramUserId, cancellationToken);
        return
        [
            await CreateCommunitiesAsync(cancellationToken),
            await CreateCampRegistrationsAsync(cancellationToken),
            await CreateCampContributionsAsync(cancellationToken),
            await CreateGatheringsAsync(cancellationToken)
        ];
    }

    private async Task<CsvExportFile> CreateCommunitiesAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.OyinQCommunities.AsNoTracking()
            .OrderBy(value => value.Key)
            .Select(value => new object?[]
            {
                value.Key, value.Name, value.TelegramChatId, value.Mode, value.TimeZoneId,
                value.IsActive, value.CreatedAt, value.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        return File("communities.csv", ["key", "name", "telegram_chat_id", "mode", "time_zone", "is_active", "created_at", "updated_at"], rows);
    }

    private async Task<CsvExportFile> CreateCampRegistrationsAsync(CancellationToken cancellationToken)
    {
        var registrations = await dbContext.CampRegistrations.AsNoTracking().Include(value => value.SelectedDays)
            .OrderBy(value => value.CampId).ThenBy(value => value.ParticipantId)
            .Select(value => new
            {
                Registration = value, CampName = value.Camp.Name, value.Participant.TelegramUserId,
                DisplayName = value.Participant.PreferredDisplayName ?? value.Participant.DisplayName
            })
            .ToListAsync(cancellationToken);
        var rows = registrations.Select(value => new object?[]
        {
            value.Registration.Id, value.Registration.CampId, value.CampName,
            value.Registration.ParticipantId, value.TelegramUserId, value.DisplayName,
            value.Registration.City, value.Registration.DaysStaying,
            string.Join(";", value.Registration.SelectedDays.OrderBy(x => x.Date).Select(x => x.Date.ToString("yyyy-MM-dd"))),
            value.Registration.NeedsAccommodation, value.Registration.CreatedAt, value.Registration.UpdatedAt
        });
        return File("camp-registrations.csv", CampRegistrationHeaders, rows);
    }

    private async Task<CsvExportFile> CreateCampContributionsAsync(CancellationToken cancellationToken)
    {
        var contributions = await dbContext.CampGameContributions.AsNoTracking()
            .Include(value => value.Camp).Include(value => value.Participant)
            .OrderBy(value => value.CampId).ThenBy(value => value.ParticipantId).ThenBy(value => value.BggId)
            .ToArrayAsync(cancellationToken);
        var rows = contributions.Select(value =>
            {
                var parentIds = (value.ReadSnapshot().ParentBggIds ?? [])
                    .Concat(value.ParentBggId is { } parent ? [parent] : [])
                    .Distinct().Order();
                return new object?[]
                {
                value.Id, value.CampId, value.Camp.Name, value.ParticipantId,
                value.Participant.TelegramUserId, value.BggId, value.ItemType,
                value.Source, value.Commitment, value.ParentBggId, string.Join(";", parentIds),
                value.CreatedAt, value.UpdatedAt
                };
            });
        return File("camp-contributions.csv", CampContributionHeaders, rows);
    }

    private async Task<CsvExportFile> CreateGatheringsAsync(CancellationToken cancellationToken)
    {
        var gatherings = await dbContext.GameGatherings.AsNoTracking()
            .Include(value => value.OrganizerParticipant)
            .Include(value => value.Participants).ThenInclude(value => value.Participant)
            .OrderBy(value => value.StartsAtUtc)
            .ToListAsync(cancellationToken);
        var rows = gatherings.Select(value =>
        {
            var snapshot = GatheringGameSnapshotSerializer.Deserialize(value.GameSnapshotJson);
            return new object?[]
            {
                value.Id, value.PublicId, value.CommunityKey, snapshot.Name,
                value.OrganizerParticipantId, value.OrganizerParticipant.TelegramUserId,
                ParticipantPresentation.GetDisplayName(value.OrganizerParticipant),
                value.StartsAtUtc, value.MinimumPlayers, value.DesiredPlayers, value.MaximumPlayers,
                value.Status,
                value.Participants.Count(participant => participant.Status == Data.Entities.GatheringParticipationStatus.Confirmed),
                value.Participants.Count(participant => participant.Status == Data.Entities.GatheringParticipationStatus.Waitlisted),
                value.CreatedAt, value.UpdatedAt, value.CompletedAt, value.CancelledAt
            };
        });
        return File("gatherings.csv", ["id", "public_id", "community_key", "game_name", "organizer_participant_id", "organizer_telegram_user_id", "organizer_name", "starts_at_utc", "minimum_players", "desired_players", "maximum_players", "status", "confirmed_count", "waitlisted_count", "created_at", "updated_at", "completed_at", "cancelled_at"], rows);
    }

    private static CsvExportFile File(string name, IReadOnlyList<string> headers, IEnumerable<object?[]> rows) =>
        new(name, BuildCsv(headers, rows));

    private static MemoryStream BuildCsv(IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(true), 1024, leaveOpen: true);
        WriteRow(writer, headers);
        foreach (var row in rows) WriteRow(writer, row);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    private static void WriteRow(TextWriter writer, IEnumerable<object?> values) =>
        writer.WriteLine(string.Join(",", values.Select(FormatCsvValue)));

    private static string FormatCsvValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private async Task EnsureAdminAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        if (!await administratorStore.IsAdministratorAsync(telegramUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Экспорт доступен только администратору.");
        }
    }
}
