using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Admin;

public sealed record CsvExportFile(string FileName, MemoryStream Content);

public sealed class CsvExportService(
    AppDbContext dbContext,
    IOptions<CampOptions> campOptions)
{
    public async Task<IReadOnlyList<CsvExportFile>> CreateAllAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        EnsureAdmin(telegramUserId);

        return
        [
            await CreateParticipantsAsync(cancellationToken),
            await CreateGamesAsync(cancellationToken),
            await CreateInterestsAsync(cancellationToken),
            await CreateSessionsAsync(cancellationToken)
        ];
    }

    private async Task<CsvExportFile> CreateParticipantsAsync(CancellationToken cancellationToken)
    {
        var participants = await dbContext.Participants
            .AsNoTracking()
            .Where(value => value.DaysStaying.HasValue
                && value.DaysStaying.Value >= 1
                && value.DaysStaying.Value <= 3
                && value.NeedsAccommodation.HasValue)
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.TelegramUserId,
                value.TelegramUsername,
                value.DisplayName,
                value.DaysStaying,
                value.NeedsAccommodation,
                value.CreatedAt,
                value.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var rows = participants.Select(value => new object?[]
        {
            value.Id,
            value.TelegramUserId,
            value.TelegramUsername,
            value.DisplayName,
            value.DaysStaying,
            value.NeedsAccommodation,
            value.CreatedAt,
            value.UpdatedAt
        });

        return new CsvExportFile(
            "participants.csv",
            BuildCsv(
                ["id", "telegram_user_id", "telegram_username", "display_name", "days_staying", "needs_accommodation", "created_at", "updated_at"],
                rows));
    }

    private async Task<CsvExportFile> CreateGamesAsync(CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.Name,
                value.BggId,
                value.TeseraAlias,
                value.MinPlayers,
                value.MaxPlayers,
                value.BestPlayers,
                value.ExternalUrl,
                ClubCopies = value.Copies.Count(copy => copy.Source == GameCopySource.Club),
                PersonalBringingCopies = value.Copies.Count(copy =>
                    copy.Source == GameCopySource.Personal
                    && copy.BringStatus == BringStatus.Bringing),
                PersonalMaybeCopies = value.Copies.Count(copy =>
                    copy.Source == GameCopySource.Personal
                    && copy.BringStatus == BringStatus.Maybe),
                InterestCount = value.Interests.Count()
            })
            .ToListAsync(cancellationToken);

        var rows = games.Select(value => new object?[]
        {
            value.Id,
            value.Name,
            value.BggId,
            value.TeseraAlias,
            value.MinPlayers,
            value.MaxPlayers,
            value.BestPlayers,
            value.ExternalUrl,
            value.ClubCopies,
            value.PersonalBringingCopies,
            value.PersonalMaybeCopies,
            value.InterestCount
        });

        return new CsvExportFile(
            "games.csv",
            BuildCsv(
                ["id", "name", "bgg_id", "tesera_alias", "min_players", "max_players", "best_players", "external_url", "club_copies", "personal_bringing_copies", "personal_maybe_copies", "interest_count"],
                rows));
    }

    private async Task<CsvExportFile> CreateInterestsAsync(CancellationToken cancellationToken)
    {
        var interests = await dbContext.GameInterests
            .AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.ParticipantId,
                value.Participant.TelegramUserId,
                value.Participant.DisplayName,
                value.GameId,
                GameName = value.Game.Name,
                value.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var rows = interests.Select(value => new object?[]
        {
            value.Id,
            value.ParticipantId,
            value.TelegramUserId,
            value.DisplayName,
            value.GameId,
            value.GameName,
            value.CreatedAt
        });

        return new CsvExportFile(
            "interests.csv",
            BuildCsv(
                ["id", "participant_id", "telegram_user_id", "participant_name", "game_id", "game_name", "created_at"],
                rows));
    }

    private async Task<CsvExportFile> CreateSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = await dbContext.GameSessions
            .AsNoTracking()
            .Include(value => value.Game)
            .Include(value => value.HostParticipant)
            .Include(value => value.Participants)
            .ThenInclude(value => value.Participant)
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);

        var rows = sessions.Select(value => new object?[]
        {
            value.Id,
            value.GameId,
            value.Game.Name,
            value.HostParticipantId,
            value.HostParticipant.TelegramUserId,
            value.HostParticipant.DisplayName,
            value.WantedAdditionalPlayers,
            value.Status,
            value.Participants.Count,
            string.Join(" | ", value.Participants
                .OrderBy(participant => participant.JoinedAt)
                .Select(participant => participant.Participant.DisplayName)),
            value.TelegramChatId,
            value.TelegramMessageId,
            value.CreatedAt,
            value.UpdatedAt,
            value.ClosedAt
        });

        return new CsvExportFile(
            "sessions.csv",
            BuildCsv(
                ["id", "game_id", "game_name", "host_participant_id", "host_telegram_user_id", "host_name", "wanted_additional_players", "status", "participant_count", "participants", "telegram_chat_id", "telegram_message_id", "created_at", "updated_at", "closed_at"],
                rows));
    }

    private static MemoryStream BuildCsv(
        IReadOnlyList<string> headers,
        IEnumerable<object?[]> rows)
    {
        var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1024,
            leaveOpen: true);

        WriteRow(writer, headers);
        foreach (var row in rows)
        {
            WriteRow(writer, row);
        }

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

    private void EnsureAdmin(long telegramUserId)
    {
        if (!campOptions.Value.AdminTelegramIds.Contains(telegramUserId))
        {
            throw new UnauthorizedAccessException("Экспорт доступен только администратору.");
        }
    }
}
