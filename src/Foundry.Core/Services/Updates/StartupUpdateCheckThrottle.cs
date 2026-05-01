namespace Foundry.Core.Services.Updates;

public static class StartupUpdateCheckThrottle
{
    public static bool ShouldRun(DateTimeOffset? lastCheckedAt, DateTimeOffset now, TimeSpan interval)
    {
        if (lastCheckedAt is null)
        {
            return true;
        }

        if (lastCheckedAt.Value > now)
        {
            return true;
        }

        return now - lastCheckedAt.Value >= interval;
    }

    public static DateTimeOffset GetNextCheckAt(DateTimeOffset lastCheckedAt, TimeSpan interval)
    {
        return lastCheckedAt.Add(interval);
    }
}
