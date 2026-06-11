using TmSync.Core.Models;

namespace TmSync.Core.Abstractions;

/// <summary>Persistent store for user mappings, record links and incremental sync state.</summary>
public interface IStateStore
{
    // User mappings
    IReadOnlyList<SyncUser> ListUsers();
    SyncUser? GetUser(string staffCode);
    void AddOrUpdateUser(SyncUser user);
    bool RemoveUser(string staffCode, bool purgeState);

    // Record links
    SyncLink? FindLinkByTmId(string moduleKey, string staffCode, string tmId);
    SyncLink? FindLinkByGraphId(string moduleKey, string staffCode, string graphId);
    void UpsertLink(SyncLink link);
    void RemoveLink(string moduleKey, string staffCode, string tmId);

    // Incremental sync state
    ModuleState GetModuleState(string moduleKey, string staffCode);
    void SaveModuleState(string moduleKey, string staffCode, string? deltaToken, DateTime? tmWatermarkUtc, DateTime lastRunUtc);

    // Email journaling dedupe
    bool IsJournaled(string staffCode, string internetMessageId);
    void MarkJournaled(string staffCode, string internetMessageId);

    // Sync log
    void AddLogEntry(DateTime timestampUtc, string level, string source, string message);
    IReadOnlyList<LogEntry> QueryLog(int limit, int minLevelRank = 0, string? search = null);
    int PurgeLogsOlderThan(int days);
}
