namespace TmSync.Core.Models;

public enum SyncModule
{
    Calendar,
    Contacts,
    Tasks,
    EmailJournal
}

public enum SyncDirection
{
    /// <summary>Changes flow both ways (default).</summary>
    TwoWay,

    /// <summary>One-way: Time Matters is the source; changes made in Microsoft 365 are not copied back.</summary>
    TimeMattersToM365,

    /// <summary>One-way: Microsoft 365 is the source; changes made in Time Matters are not copied back.</summary>
    M365ToTimeMatters
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
