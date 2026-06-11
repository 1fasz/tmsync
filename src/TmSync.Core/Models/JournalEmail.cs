namespace TmSync.Core.Models;

/// <summary>An Office 365 email captured for journaling into Time Matters.</summary>
public sealed class JournalEmail
{
    public string? InternetMessageId { get; set; }

    /// <summary>"Incoming" or "Outgoing".</summary>
    public string Direction { get; set; } = "Incoming";

    public string? From { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Subject { get; set; }
    public DateTime SentUtc { get; set; }
    public string? BodyText { get; set; }
}
