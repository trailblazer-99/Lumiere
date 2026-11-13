namespace LumiereMediaPlayer.Helpers;

public static class TimeFormatting
{
    public static string Format(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        return $"{time.Minutes}:{time.Seconds:D2}";
    }
}
