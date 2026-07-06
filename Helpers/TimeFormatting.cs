namespace FluentMediaPlayer.Helpers;

public static class TimeFormatting
{
    public static string Format(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return time.ToString(@"h\:mm\:ss");
        }

        return time.ToString(@"m\:ss");
    }
}
