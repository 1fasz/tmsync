namespace TmSync.Core.Models;

/// <summary>Canonical representation of a calendar event shared by both sides of the sync.</summary>
public sealed class CalendarItem
{
    public string? Subject { get; set; }
    public string? BodyText { get; set; }
    public string? Location { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool AllDay { get; set; }
    public int? ReminderMinutes { get; set; }
}
