using TmSync.Core.Models;

namespace TmSync.Core.Config;

/// <summary>Bound from the "Sync" section of appsettings.json.</summary>
public sealed class TmSyncOptions
{
    public const string SectionName = "Sync";

    public string StateDatabasePath { get; set; } = "tmsync-state.db";
    public int IntervalMinutes { get; set; } = 5;
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.NewestWins;

    /// <summary>Default sync direction; can be overridden per user via the CLI.</summary>
    public SyncDirection Direction { get; set; } = SyncDirection.TwoWay;

    public bool PropagateDeletes { get; set; } = true;

    /// <summary>Sync log entries older than this many days are purged automatically.</summary>
    public int LogRetentionDays { get; set; } = 30;
    public ModuleToggles Modules { get; set; } = new();
    public CalendarWindow Calendar { get; set; } = new();

    public sealed class ModuleToggles
    {
        public bool Calendar { get; set; } = true;
        public bool Contacts { get; set; } = true;
        public bool Tasks { get; set; } = true;
        public bool EmailJournal { get; set; } = false;
    }

    public sealed class CalendarWindow
    {
        /// <summary>How far back the synced calendar window reaches.</summary>
        public int PastDays { get; set; } = 30;

        /// <summary>How far forward the synced calendar window reaches.</summary>
        public int FutureDays { get; set; } = 365;
    }
}

/// <summary>Bound from the "Microsoft365" section of appsettings.json.</summary>
public sealed class Microsoft365Options
{
    public const string SectionName = "Microsoft365";

    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

/// <summary>Bound from the "TimeMatters" section of appsettings.json.</summary>
public sealed class TimeMattersOptions
{
    public const string SectionName = "TimeMatters";

    public string ConnectionString { get; set; } = "";
}
