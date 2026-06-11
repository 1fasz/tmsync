using Microsoft.Extensions.Logging.Abstractions;
using TmSync.Core.Models;
using TmSync.Core.State;
using TmSync.Core.Sync;

namespace TmSync.Core.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private static readonly SyncUser User = new("JDOE", "jdoe@example.com");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tmsync-test-{Guid.NewGuid():N}.db");
    private readonly StateStore _state;
    private readonly FakeTmStore _tm = new();
    private readonly FakeGraphStore _graph = new();

    public SyncEngineTests() => _state = new StateStore(_dbPath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private SyncEngine<CalendarItem> CreateEngine(SyncEngineOptions? options = null) => new(
        SyncModule.Calendar, _tm, _graph, _state, TestData.NaturalKey,
        options ?? new SyncEngineOptions(), NullLogger.Instance);

    [Fact]
    public async Task TimeMattersItem_FlowsToMicrosoft365()
    {
        var tmId = _tm.Seed(TestData.Event("Court hearing", new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);

        var stats = await CreateEngine().RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.CreatedInGraph);
        Assert.Single(_graph.Rows);
        Assert.NotNull(_state.FindLinkByTmId("Calendar", User.StaffCode, tmId));
    }

    [Fact]
    public async Task Microsoft365Item_FlowsToTimeMatters()
    {
        var graphId = _graph.Seed(TestData.Event("Client call", new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);

        var stats = await CreateEngine().RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.CreatedInTm);
        Assert.Single(_tm.Rows);
        Assert.NotNull(_state.FindLinkByGraphId("Calendar", User.StaffCode, graphId));
    }

    [Fact]
    public async Task ExistingItemsOnBothSides_AreLinkedNotDuplicated()
    {
        // Same event already exists on both sides (left behind by the old Exchange sync).
        var item = TestData.Event("Deposition", new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc));
        var tmId = _tm.Seed(item, DateTime.UtcNow);
        var graphId = _graph.Seed(TestData.Event("Deposition", new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);

        var stats = await CreateEngine().RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.Matched);
        Assert.Equal(0, stats.CreatedInGraph);
        Assert.Equal(0, stats.CreatedInTm);
        var link = _state.FindLinkByTmId("Calendar", User.StaffCode, tmId);
        Assert.NotNull(link);
        Assert.Equal(graphId, link!.GraphId);
    }

    [Fact]
    public async Task SecondRunWithNoChanges_DoesNothing()
    {
        _tm.Seed(TestData.Event("Hearing", new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine();
        await engine.RunAsync(User, CancellationToken.None);

        // The Graph delta now echoes back the item the engine itself created.
        foreach (var (id, row) in _graph.Rows)
            _graph.PendingChanges.Add(new(id, row.ModifiedUtc, false, row.Item));

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.False(stats.HasActivity);
        Assert.Equal(1, _graph.Creates);
        Assert.Equal(0, _graph.Updates);
        Assert.Equal(0, _tm.Creates);
        Assert.Equal(0, _tm.Updates);
    }

    [Fact]
    public async Task TimeMattersUpdate_PropagatesToMicrosoft365()
    {
        var tmId = _tm.Seed(TestData.Event("Meeting", new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine();
        await engine.RunAsync(User, CancellationToken.None);

        var updated = TestData.Event("Meeting (moved)", new DateTime(2026, 7, 5, 11, 0, 0, DateTimeKind.Utc));
        _tm.Rows[tmId] = (updated, DateTime.UtcNow.AddMinutes(1), false);

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.UpdatedInGraph);
        Assert.Equal("Meeting (moved)", _graph.Rows.Single().Value.Item.Subject);
    }

    [Fact]
    public async Task Conflict_NewestWins()
    {
        var tmId = _tm.Seed(TestData.Event("Review", new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine();
        await engine.RunAsync(User, CancellationToken.None);
        var graphId = _graph.Rows.Single().Key;

        // Both sides change; the M365 edit is newer.
        _tm.Rows[tmId] = (TestData.Event("Review (TM edit)", new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow.AddMinutes(1), false);
        var graphEdit = TestData.Event("Review (M365 edit)", new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc));
        _graph.Rows[graphId] = (graphEdit, DateTime.UtcNow.AddMinutes(5));
        _graph.PendingChanges.Add(new(graphId, DateTime.UtcNow.AddMinutes(5), false, graphEdit));

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.Conflicts);
        Assert.Equal("Review (M365 edit)", _tm.Rows[tmId].Item.Subject);
    }

    [Fact]
    public async Task TimeMattersDelete_PropagatesToMicrosoft365()
    {
        var tmId = _tm.Seed(TestData.Event("Cancelled mtg", new DateTime(2026, 7, 7, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine();
        await engine.RunAsync(User, CancellationToken.None);

        _tm.MarkDeleted(tmId, DateTime.UtcNow.AddMinutes(1));

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.DeletedInGraph);
        Assert.Empty(_graph.Rows);
        Assert.Null(_state.FindLinkByTmId("Calendar", User.StaffCode, tmId));
    }

    [Fact]
    public async Task Microsoft365Delete_PropagatesToTimeMatters()
    {
        var graphId = _graph.Seed(TestData.Event("Old appt", new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine();
        await engine.RunAsync(User, CancellationToken.None);
        Assert.Single(_tm.Rows);

        _graph.AnnounceDeleted(graphId);

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.DeletedInTm);
        Assert.Empty(_tm.Rows);
    }

    [Fact]
    public async Task OneWayTmToM365_IgnoresMicrosoft365Changes()
    {
        var options = new SyncEngineOptions { Direction = SyncDirection.TimeMattersToM365 };
        var engine = CreateEngine(options);

        // TM item still flows out to M365...
        _tm.Seed(TestData.Event("TM only", new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        // ...but a new M365 item must not be copied into Time Matters.
        _graph.Seed(TestData.Event("M365 only", new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.CreatedInGraph);
        Assert.Equal(0, stats.CreatedInTm);
        Assert.Single(_tm.Rows);

        // An M365 edit of the synced item must not flow back either.
        var graphId = _graph.Rows.Single(r => r.Value.Item.Subject == "TM only").Key;
        var edit = TestData.Event("TM only (edited in M365)", new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        _graph.Rows[graphId] = (edit, DateTime.UtcNow.AddMinutes(1));
        _graph.PendingChanges.Add(new(graphId, DateTime.UtcNow.AddMinutes(1), false, edit));

        stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(0, stats.UpdatedInTm);
        Assert.Equal("TM only", _tm.Rows.Single().Value.Item.Subject);
    }

    [Fact]
    public async Task OneWayM365ToTm_IgnoresTimeMattersChanges()
    {
        var options = new SyncEngineOptions { Direction = SyncDirection.M365ToTimeMatters };
        var engine = CreateEngine(options);

        _graph.Seed(TestData.Event("M365 source", new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        _tm.Seed(TestData.Event("TM local", new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);

        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(1, stats.CreatedInTm);
        Assert.Equal(0, stats.CreatedInGraph);
        Assert.Single(_graph.Rows);

        // A TM edit of the synced item must not flow back to M365.
        var tmId = _tm.Rows.Single(r => r.Value.Item.Subject == "M365 source").Key;
        _tm.Rows[tmId] = (TestData.Event("M365 source (edited in TM)", new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow.AddMinutes(1), false);

        stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(0, stats.UpdatedInGraph);
        Assert.Equal("M365 source", _graph.Rows.Single().Value.Item.Subject);
    }

    [Fact]
    public async Task DeletesAreNotPropagated_WhenDisabled()
    {
        var options = new SyncEngineOptions { PropagateDeletes = false };
        var tmId = _tm.Seed(TestData.Event("Keep me", new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)), DateTime.UtcNow);
        var engine = CreateEngine(options);
        await engine.RunAsync(User, CancellationToken.None);

        _tm.MarkDeleted(tmId, DateTime.UtcNow.AddMinutes(1));
        var stats = await engine.RunAsync(User, CancellationToken.None);

        Assert.Equal(0, stats.DeletedInGraph);
        Assert.Single(_graph.Rows);
    }
}
