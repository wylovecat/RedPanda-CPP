using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscSubmissionGuardTests
{
    [Fact]
    public void HashCode_IsStableAndSensitive()
    {
        var first = AscSubmissionGuard.HashCode("int main() {}");
        var second = AscSubmissionGuard.HashCode("int main() {}");
        var other = AscSubmissionGuard.HashCode("int main() { return 0; }");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void ResolveDetailId_PrefersNumericRowIdOverRequestHandle()
    {
        var result = new AscSubmissionResult { Id = "2635935" };

        Assert.Equal(
            "2635935",
            AscSubmissionGuard.ResolveDetailId(result, "56320d39d37b3642ced06a1bb0658247"));

        result.Id = string.Empty;
        Assert.Equal(
            "56320d39d37b3642ced06a1bb0658247",
            AscSubmissionGuard.ResolveDetailId(result, "56320d39d37b3642ced06a1bb0658247"));
    }

    [Fact]
    public void IsRecentDuplicate_OnlyMatchesSameHashInsideWindow()
    {
        var now = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        var hash = AscSubmissionGuard.HashCode("code");
        var link = new AscProblemLink
        {
            DisplayId = "A1",
            LastSubmittedCodeHash = hash,
            LastSubmittedAtUtc = now.AddSeconds(-30)
        };

        Assert.True(AscSubmissionGuard.IsRecentDuplicate(link, hash, now));

        link.LastSubmittedAtUtc = now.AddSeconds(-120);
        Assert.False(AscSubmissionGuard.IsRecentDuplicate(link, hash, now));

        link.LastSubmittedAtUtc = now.AddSeconds(-10);
        Assert.False(AscSubmissionGuard.IsRecentDuplicate(
            link,
            AscSubmissionGuard.HashCode("other"),
            now));

        link.LastSubmittedCodeHash = string.Empty;
        Assert.False(AscSubmissionGuard.IsRecentDuplicate(link, hash, now));
    }
}
