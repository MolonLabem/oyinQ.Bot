using oyinQ.Bot.Features.Changelog;

namespace oyinQ.Bot.Tests;

public sealed class ChangelogContentTests
{
    [Fact]
    public void LatestReleaseUsesNewestDatedSectionAndReadableTelegramText()
    {
        var release = ChangelogContent.LatestFrom("# Изменения\n\n## Unreleased\nЧерновик\n\n## 2026-09-07\nСтарое\n\n## 2026-09-16\n\n### Игры\n\n- Команда `/help`\n- <клуб> & друзья\n\n## 2026-09-10\nДругое");
        Assert.Equal("2026-09-16", release.Date);
        Assert.Equal("🎲 Что нового в OyinQ · 2026-09-16\n\nИгры\n\n• Команда /help\n• <клуб> & друзья", release.Text);
        Assert.StartsWith("2026-09-16-", release.Id);
        Assert.InRange(release.Id.Length, 1, 64);
    }

    [Fact]
    public void SameDayEditsChangeIdentityButLineEndingsAndOlderHistoryDoNot()
    {
        const string markdown = "## 2026-09-16\n\n- Новое\n\n## 2026-09-07\n\n- Старое\n";
        var release = ChangelogContent.LatestFrom(markdown);
        Assert.Equal(release, ChangelogContent.LatestFrom(markdown.Replace("\n", "\r\n")));
        Assert.Equal(release, ChangelogContent.LatestFrom(markdown.Replace("Старое", "Старая правка")));
        Assert.NotEqual(release.Id, ChangelogContent.LatestFrom(markdown.Replace("Новое", "Новое исправление")).Id);
    }

    [Theory]
    [InlineData("# Нет выпуска")]
    [InlineData("## 2026-02-30\n- Неверная дата")]
    [InlineData("## 2026-09-16\n\n## 2026-09-07\n- Старое")]
    public void MissingOrEmptyLatestReleaseNeverFallsBackToOldContent(string markdown) =>
        Assert.Throws<InvalidOperationException>(() => ChangelogContent.LatestFrom(markdown));

    [Fact]
    public void OversizedReleaseIsRejectedWithoutTruncation() =>
        Assert.Throws<InvalidOperationException>(() => ChangelogContent.LatestFrom("## 2026-09-16\n" + new string('x', 3500)));

    [Fact]
    public void EmbeddedChangelogAlwaysSuppliesTheCurrentBoundedRelease()
    {
        Assert.Equal(ChangelogContent.LatestFrom(ChangelogContent.Markdown), ChangelogContent.Latest);
        Assert.InRange(ChangelogContent.Latest.Text.Length, 1, ChangelogContent.MaxAnnouncementLength);
    }
}
