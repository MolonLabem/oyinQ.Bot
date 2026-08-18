using Microsoft.Extensions.Configuration;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Tests;

public sealed class BggOptionsTests
{
    [Fact]
    public void FromConfiguration_WithoutToken_DisablesBgg()
    {
        var configuration = new ConfigurationManager();

        var options = BggOptions.FromConfiguration(configuration);

        Assert.False(options.IsAvailable);
        Assert.Equal(string.Empty, options.ApiToken);
    }

    [Fact]
    public void FromConfiguration_WithToken_EnablesBggAndTrimsValue()
    {
        var configuration = new ConfigurationManager
        {
            ["BGG_API_TOKEN"] = "  test-token  "
        };

        var options = BggOptions.FromConfiguration(configuration);

        Assert.True(options.IsAvailable);
        Assert.Equal("test-token", options.ApiToken);
    }
}
