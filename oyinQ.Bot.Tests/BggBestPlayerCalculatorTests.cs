using System.Xml.Linq;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggBestPlayerCalculatorTests
{
    [Fact]
    public void Calculate_SingleBestPlayerCount_ReturnsSingleNumber()
    {
        var item = ParseItem("""
            <poll name="suggested_numplayers">
              <results numplayers="2">
                <result value="Best" numvotes="10" />
                <result value="Recommended" numvotes="4" />
                <result value="Not Recommended" numvotes="1" />
              </results>
              <results numplayers="3">
                <result value="Best" numvotes="2" />
                <result value="Recommended" numvotes="8" />
                <result value="Not Recommended" numvotes="1" />
              </results>
            </poll>
            """);

        Assert.Equal("2", BggBestPlayerCalculator.Calculate(item));
    }

    [Fact]
    public void Calculate_ConsecutiveBestCounts_CollapsesRange()
    {
        var item = ParseItem("""
            <poll name="suggested_numplayers">
              <results numplayers="2"><result value="Best" numvotes="8" /><result value="Recommended" numvotes="4" /><result value="Not Recommended" numvotes="1" /></results>
              <results numplayers="3"><result value="Best" numvotes="8" /><result value="Recommended" numvotes="5" /><result value="Not Recommended" numvotes="2" /></results>
              <results numplayers="4"><result value="Best" numvotes="7" /><result value="Recommended" numvotes="7" /><result value="Not Recommended" numvotes="2" /></results>
            </poll>
            """);

        Assert.Equal("2–4", BggBestPlayerCalculator.Calculate(item));
    }

    [Fact]
    public void Calculate_SeparatedBestCounts_CollapsesRangesAndKeepsSingles()
    {
        var item = ParseItem("""
            <poll name="suggested_numplayers">
              <results numplayers="2"><result value="Best" numvotes="8" /><result value="Recommended" numvotes="4" /><result value="Not Recommended" numvotes="1" /></results>
              <results numplayers="3"><result value="Best" numvotes="8" /><result value="Recommended" numvotes="4" /><result value="Not Recommended" numvotes="1" /></results>
              <results numplayers="4"><result value="Best" numvotes="8" /><result value="Recommended" numvotes="4" /><result value="Not Recommended" numvotes="1" /></results>
              <results numplayers="5"><result value="Best" numvotes="2" /><result value="Recommended" numvotes="8" /><result value="Not Recommended" numvotes="1" /></results>
              <results numplayers="6"><result value="Best" numvotes="6" /><result value="Recommended" numvotes="6" /><result value="Not Recommended" numvotes="2" /></results>
            </poll>
            """);

        Assert.Equal("2–4, 6", BggBestPlayerCalculator.Calculate(item));
    }

    private static XElement ParseItem(string poll) =>
        XDocument.Parse($"<item>{poll}</item>").Root!;
}
