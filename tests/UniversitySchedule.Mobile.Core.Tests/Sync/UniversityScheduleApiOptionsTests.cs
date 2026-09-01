using UniversitySchedule.Mobile.Core.Sync;

namespace UniversitySchedule.Mobile.Core.Tests.Sync;

public sealed class UniversityScheduleApiOptionsTests
{
    [Theory]
    [InlineData("android")]
    [InlineData("ios")]
    [InlineData("web")]
    [InlineData(" WEB ")]
    public void IsEnabled_AcceptsSupportedHttpsPlatforms(string platform)
    {
        var options = new UniversityScheduleApiOptions(
            new Uri("https://api.example.test/"),
            platform,
            "1.0.0");

        Assert.True(options.IsEnabled);
    }

    [Theory]
    [InlineData("http://api.example.test/", "web", "1.0.0")]
    [InlineData("https://api.example.test/", "desktop", "1.0.0")]
    [InlineData("https://api.example.test/", "web", "")]
    public void IsEnabled_RejectsUnsafeOrIncompleteConfiguration(
        string baseAddress,
        string platform,
        string appVersion)
    {
        var options = new UniversityScheduleApiOptions(new Uri(baseAddress), platform, appVersion);

        Assert.False(options.IsEnabled);
    }
}
