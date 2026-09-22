using System.Diagnostics;

namespace CotwLiveTracker.Infrastructure;

internal static class GameProcessLocator
{
    public static Process? Find(string processName)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(processName);

        return Process
            .GetProcessesByName(normalizedName)
            .OrderByDescending(static process =>
            {
                try
                {
                    return process.StartTime;
                }
                catch
                {
                    return DateTime.MinValue;
                }
            })
            .FirstOrDefault();
    }
}
