namespace Flux.Ui;

/// <summary>Human-friendly relative timestamps ("just now", "2h ago", "yesterday").</summary>
public static class TimeFormat
{
    public static string Relative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when.ToLocalTime();
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        return delta switch
        {
            { TotalSeconds: < 45 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)delta.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)delta.TotalHours}h ago",
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 7 } => $"{(int)delta.TotalDays} days ago",
            _ => when.ToLocalTime().ToString("d MMM yyyy"),
        };
    }
}
