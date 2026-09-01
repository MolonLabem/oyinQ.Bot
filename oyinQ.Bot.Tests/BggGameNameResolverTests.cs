using System.Xml.Linq;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggGameNameResolverTests
{
    [Fact]
    public void Resolve_UsesCanonicalNameFromRussianOnlyVersion()
    {
        var result = Resolve("""
            <item><name type="primary" value="Terraforming Mars" /><versions><item>
              <name type="primary" value="Russian edition" />
              <canonicalname value="Покорение Марса" />
              <link type="language" id="2202" value="Russian" />
            </item></versions></item>
            """);

        Assert.Equal("Покорение Марса", result.DisplayName);
        Assert.Equal("Terraforming Mars", result.OriginalName);
        Assert.Equal("Покорение Марса", result.RussianName);
    }

    [Fact]
    public void Resolve_FallsBackWhenRussianVersionHasNoCanonicalTitle()
    {
        var result = Resolve("""
            <item><name type="primary" value="Terraforming Mars" /><versions><item>
              <name type="primary" value="Russian edition" />
              <link type="language" id="2202" value="Russian" />
            </item></versions></item>
            """);

        Assert.Equal("Terraforming Mars", result.DisplayName);
        Assert.Null(result.RussianName);
    }

    [Fact]
    public void Resolve_FallsBackWhenLocalizedTitleIsEmpty()
    {
        var result = Resolve("""
            <item><name type="primary" value="Original" /><versions><item>
              <canonicalname value=" " />
              <link type="language" id="2202" value="Russian" />
            </item></versions></item>
            """);

        Assert.Equal("Original", result.DisplayName);
        Assert.Equal("Original", result.OriginalName);
        Assert.Null(result.RussianName);
    }

    [Fact]
    public void Resolve_DoesNotGuessFromUnassociatedAlternateNames()
    {
        var result = Resolve("""
            <item><name type="primary" value="Terraforming Mars" />
              <name type="alternate" value="Покорение Марса" />
            </item>
            """);

        Assert.Equal("Terraforming Mars", result.DisplayName);
        Assert.Null(result.RussianName);
    }

    [Fact]
    public void Resolve_FallsBackForMultilingualOrAmbiguousRussianVersions()
    {
        var multilingual = Resolve("""
            <item><name type="primary" value="Original" /><versions><item>
              <canonicalname value="Русское имя" />
              <link type="language" id="2202" value="Russian" />
              <link type="language" id="2184" value="English" />
            </item></versions></item>
            """);
        var ambiguous = Resolve("""
            <item><name type="primary" value="Original" /><versions>
              <item><canonicalname value="Имя один" /><link type="language" id="2202" value="Russian" /></item>
              <item><canonicalname value="Имя два" /><link type="language" id="2202" value="Russian" /></item>
            </versions></item>
            """);

        Assert.Equal("Original", multilingual.DisplayName);
        Assert.Equal("Original", ambiguous.DisplayName);
        Assert.Null(multilingual.RussianName);
        Assert.Null(ambiguous.RussianName);
    }

    private static BggResolvedName Resolve(string xml) =>
        BggGameNameResolver.Resolve(XElement.Parse(xml))!;
}
