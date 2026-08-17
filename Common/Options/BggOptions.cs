namespace oyinQ.Bot.Common.Options;

public sealed class BggOptions
{
    public string ApiToken { get; init; } = string.Empty;

    public static BggOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            ApiToken = configuration["BGG_API_TOKEN"]?.Trim() ?? string.Empty
        };
}
