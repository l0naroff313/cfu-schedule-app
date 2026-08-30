namespace UniversitySchedule.Infrastructure.Cfu;

public sealed class CfuScheduleCacheOptions
{
    public const string SectionName = "CfuSchedule";

    public int FreshCacheMinutes { get; init; } = 5;

    public TimeSpan FreshFor => TimeSpan.FromMinutes(FreshCacheMinutes);
}
