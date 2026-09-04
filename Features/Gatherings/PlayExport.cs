using System.Globalization;
using System.Text.Json;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record PortablePlayPlayer(Guid Id, string Name, decimal? Score, bool IsWinner);
public sealed record PortablePlay(Guid PublicId, string GameName, long? BggId, DateTimeOffset EndedAtUtc,
    string Location, int? DurationMinutes, IReadOnlyList<PortablePlayPlayer> Players,
    IReadOnlyList<Collections.ClubCollectionExpansion> Expansions, int Version = 1, string TimeZoneId = "UTC",
    bool HigherScoreWins = true);

public static class PlayExport
{
    public static PortablePlay From(GatheringPlayRecord record)
    {
        if (!record.WasPlayed || record.EndedAtUtc is null) throw new InvalidOperationException("Сначала подтвердите состоявшуюся партию.");
        var game = GatheringGameSnapshotSerializer.Deserialize(record.GameSnapshotJson);
        return new(record.PublicId, game.Name, game.BggId, record.EndedAtUtc.Value.ToUniversalTime(),
            string.IsNullOrWhiteSpace(record.Location) ? record.Gathering.Community.Name : record.Location, record.DurationMinutes,
            record.Players.OrderBy(x => x.SourcePlayerId).Select(x => new PortablePlayPlayer(
                x.SourcePlayerId, x.DisplayName, x.Score, x.IsWinner)).ToArray(),
            game.SelectedExpansions, TimeZoneId: record.Gathering.Community.TimeZoneId,
            HigherScoreWins: record.HigherScoreWins);
    }
}

// Official createPlay contract: playDate is the UTC END time, not the scheduled start.
// https://www.bgstatsapp.com/support/push-plays-to-bg-stats-from-other-apps-or-websites/
public static class BgStatsPlayExportAdapter
{
    public static string Build(PortablePlay play)
    {
        var data = new
        {
            sourceName = "OyinQ", sourcePlayId = play.PublicId.ToString("N"),
            playDate = play.EndedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            game = new { name = play.GameName, sourceGameId = play.BggId?.ToString(CultureInfo.InvariantCulture) ?? play.PublicId.ToString("N"),
                bggId = play.BggId, highestWins = play.HigherScoreWins, noPoints = play.Players.All(x => x.Score is null) },
            players = play.Players.Select(x => new { name = x.Name, sourcePlayerId = x.Id.ToString("N"),
                score = x.Score, winner = x.IsWinner }),
            durationMin = play.DurationMinutes, location = play.Location,
            comments = play.Expansions.Count == 0 ? null : "Дополнения: " + string.Join(", ", play.Expansions.Select(x => x.Name))
        };
        return "https://app.bgstatsapp.com/createPlay.html?data=" + Uri.EscapeDataString(JsonSerializer.Serialize(data,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
    }
}
