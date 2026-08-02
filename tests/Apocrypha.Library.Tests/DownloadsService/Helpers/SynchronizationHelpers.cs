namespace Apocrypha.Library.Tests.DownloadsService.Helpers;

public static class SynchronizationHelpers
{
    /// <summary>
    /// Waits until the given condition becomes true.
    /// </summary>
    public static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }
}
