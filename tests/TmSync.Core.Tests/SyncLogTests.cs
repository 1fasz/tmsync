using Microsoft.Extensions.Logging;
using TmSync.Core.State;

namespace TmSync.Core.Tests;

public sealed class SyncLogTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tmsync-logtest-{Guid.NewGuid():N}.db");
    private readonly StateStore _state;

    public SyncLogTests() => _state = new StateStore(_dbPath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
    }

    [Fact]
    public void LogEntries_AreStoredAndFiltered()
    {
        _state.AddLogEntry(DateTime.UtcNow, "Information", "SyncEngine", "Calendar/JDOE: synced 3 items");
        _state.AddLogEntry(DateTime.UtcNow, "Warning", "SyncEngine", "Contacts/JDOE: delta token expired");
        _state.AddLogEntry(DateTime.UtcNow, "Error", "SyncRunner", "Tasks/MSMITH: update failed");

        Assert.Equal(3, _state.QueryLog(100).Count);
        Assert.Equal(2, _state.QueryLog(100, minLevelRank: 3).Count);          // warning+
        Assert.Single(_state.QueryLog(100, minLevelRank: 4));                  // error only
        Assert.Single(_state.QueryLog(100, search: "MSMITH"));
        Assert.Empty(_state.QueryLog(100, search: "nomatch"));
    }

    [Fact]
    public void QueryLog_ReturnsNewestFirst_AndHonorsLimit()
    {
        for (var i = 0; i < 10; i++)
            _state.AddLogEntry(DateTime.UtcNow, "Information", "Test", $"entry {i}");

        var entries = _state.QueryLog(5);
        Assert.Equal(5, entries.Count);
        Assert.Equal("entry 9", entries[0].Message);
    }

    [Fact]
    public void PurgeLogsOlderThan_RemovesOldEntries()
    {
        _state.AddLogEntry(DateTime.UtcNow.AddDays(-40), "Information", "Test", "old");
        _state.AddLogEntry(DateTime.UtcNow, "Information", "Test", "recent");

        var removed = _state.PurgeLogsOlderThan(30);

        Assert.Equal(1, removed);
        Assert.Equal("recent", Assert.Single(_state.QueryLog(10)).Message);
    }

    [Fact]
    public void LoggerProvider_PersistsTmSyncLogsOnly()
    {
        using var provider = new StateStoreLoggerProvider(_state);

        var tmLogger = provider.CreateLogger("TmSync.Core.Sync.SyncEngine");
        tmLogger.LogInformation("Calendar/JDOE: did things");
        tmLogger.LogDebug("too detailed, should be skipped");

        var frameworkLogger = provider.CreateLogger("Microsoft.Hosting.Lifetime");
        frameworkLogger.LogInformation("framework noise, should be skipped");

        var entry = Assert.Single(_state.QueryLog(10));
        Assert.Equal("SyncEngine", entry.Source);
        Assert.Equal("Calendar/JDOE: did things", entry.Message);
    }
}
