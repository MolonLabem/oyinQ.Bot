using Microsoft.Extensions.Configuration;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Tests;

public sealed class AdministrationOptionsTests
{
    [Fact]
    public void FromConfiguration_ParsesCommaSeparatedBootstrapIds()
    {
        var configuration = new ConfigurationManager
        {
            ["Administration:BootstrapTelegramUserIds"] = " 42, 99,42 "
        };

        var options = AdministrationOptions.FromConfiguration(configuration);

        Assert.Equal([42L, 99L], options.BootstrapTelegramUserIds.Order());
    }

    [Fact]
    public void FromConfiguration_AllowsNoBootstrapIds()
    {
        var options = AdministrationOptions.FromConfiguration(new ConfigurationManager());

        Assert.Empty(options.BootstrapTelegramUserIds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-an-id")]
    public void FromConfiguration_RejectsInvalidBootstrapIds(string value)
    {
        var configuration = new ConfigurationManager
        {
            ["Administration:BootstrapTelegramUserIds"] = value
        };

        Assert.Throws<InvalidOperationException>(() =>
            AdministrationOptions.FromConfiguration(configuration));
    }
}
