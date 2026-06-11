namespace TmSync.Core.Models;

public enum SyncModule
{
    Calendar,
    Contacts,
    Tasks,
    EmailJournal
}

public enum ConflictPolicy
{
    /// <summary>The side with the most recent modification wins (default).</summary>
    NewestWins,

    /// <summary>Time Matters is the source of truth in a conflict.</summary>
    TimeMattersWins,

    /// <summary>Microsoft 365 is the source of truth in a conflict.</summary>
    Microsoft365Wins
}
