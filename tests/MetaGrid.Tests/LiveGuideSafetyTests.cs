using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class LiveGuideSafetyTests
{
    [Theory]
    [InlineData("Live")]
    [InlineData("Cached live")]
    public async Task UnverifiedLiveMapping_CannotAuthorizeSteamWrite(string classification)
    {
        var result = await new DotaGuideMappingResolver().ResolveAsync(new SteamAccount
            { AccountId = "1", SteamRootPath = "fixture", UserDataPath = "fixture", DotaConfigDirectory = "fixture" },
            new GuideSubscriptionRecord(), new NormalizedHeroGuideBuild { SourceClassification = classification },
            CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.Build);
        Assert.Contains("No Steam write", result.Error);
    }

    [Fact]
    public void ChallengePage_IsNeverParsedAsGuide()
    {
        Assert.ThrowsAny<Exception>(() => D2ptLiveGuideParser.Parse("<html>Just a moment... Enable JavaScript and cookies to continue</html>"));
    }

    [Fact]
    public void EmptyPage_IsNeverParsedAsGuide()
    {
        Assert.ThrowsAny<Exception>(() => D2ptLiveGuideParser.Parse("<html></html>"));
    }
}
