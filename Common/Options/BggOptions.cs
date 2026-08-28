namespace oyinQ.Bot.Common.Options;

public sealed class BggOptions
{
    public const string SectionName = "BoardGameGeek";

    public string ApiToken { get; init; } = string.Empty;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ApiToken);

    public static BggOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            ApiToken = configuration[$"{SectionName}:ApiToken"]?.Trim() ?? string.Empty
        };
}
