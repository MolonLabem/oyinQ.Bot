using Microsoft.Extensions.Configuration;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Tests;

public sealed class AdministrationOptionsTests
{
    [Fact]
    public void FromConfiguration_DoesNotPromoteMultipleLegacyBootstrapIds()
    {
        var configuration = new ConfigurationManager
        {
            ["Administration:BootstrapTelegramUserIds"] = " 42, 99,42 "
        };

        var options = AdministrationOptions.FromConfiguration(configuration);

        Assert.Empty(options.SuperAdminTelegramUserIds);
    }

    [Fact]
    public void FromConfiguration_AllowsNoBootstrapIds()
    {
        var options = AdministrationOptions.FromConfiguration(new ConfigurationManager());

        Assert.Empty(options.SuperAdminTelegramUserIds);
    }

    [Fact]
    public void FromConfiguration_UsesExplicitSuperAdminsWithoutPromotingMultipleLegacyAdmins()
    {
        var configuration = new ConfigurationManager
        {
            ["Administration:SuperAdminTelegramUserIds"] = "42,99",
            ["Administration:BootstrapTelegramUserIds"] = "1,2"
        };

        var options = AdministrationOptions.FromConfiguration(configuration);

        Assert.Equal([42L, 99L], options.SuperAdminTelegramUserIds.Order());
    }

    [Fact]
    public void FromConfiguration_PreservesSingleLegacyOwnerAsCompatibilitySuperAdmin()
    {
        var configuration = new ConfigurationManager
        {
            ["Administration:BootstrapTelegramUserIds"] = "42"
        };

        var options = AdministrationOptions.FromConfiguration(configuration);

        Assert.Equal([42L], options.SuperAdminTelegramUserIds);
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
