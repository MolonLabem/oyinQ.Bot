namespace oyinQ.Bot.Common.Options;

public sealed class BggOptions
{
    public string ApiToken { get; init; } = string.Empty;

    public static BggOptions FromConfiguration(IConfiguration configuration)
    {
        var apiToken = configuration["BGG_API_TOKEN"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException("BGG_API_TOKEN is required.");
        }

        return new BggOptions { ApiToken = apiToken };
    }
}
