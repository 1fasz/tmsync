using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using TmSync.Core.Sync;

namespace TmSync.Core.Tests;

/// <summary>In-memory Time Matters store. Tracks writes and serves changes by watermark.</summary>
public sealed class FakeTmStore : ITmModuleStore<CalendarItem>
{
    private int _nextId = 1;
    public Dictionary<string, (CalendarItem Item, DateTime ModifiedUtc, bool Deleted)> Rows { get; } = new();
    public int Creates, Updates, Deletes;

    public string Seed(CalendarItem item, DateTime modifiedUtc)
    {
        var id = $"TM-{_nextId++}";
        Rows[id] = (item, modifiedUtc, false);
        return id;
    }

    public void MarkDeleted(string id, DateTime modifiedUtc) =>
        Rows[id] = (Rows[id].Item, modifiedUtc, true);

    public Task<IReadOnlyList<TmRecord<CalendarItem>>> GetChangedSinceAsync(string staffCode, DateTime? sinceUtc, CancellationToken ct)
    {
        IReadOnlyList<TmRecord<CalendarItem>> result = Rows
            .Where(kv => sinceUtc is null || kv.Value.ModifiedUtc > sinceUtc)
            .Select(kv => new TmRecord<CalendarItem>(kv.Key, kv.Value.ModifiedUtc, kv.Value.Deleted, kv.Value.Deleted ? null : kv.Value.Item))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<string> CreateAsync(string staffCode, CalendarItem item, CancellationToken ct)
    {
        Creates++;
        return Task.FromResult(Seed(item, DateTime.UtcNow));
    }

    public Task UpdateAsync(string tmId, CalendarItem item, CancellationToken ct)
    {
        Updates++;
        Rows[tmId] = (item, DateTime.UtcNow, false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tmId, CancellationToken ct)
    {
        Deletes++;
        Rows.Remove(tmId);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory Microsoft 365 store with a simplistic delta feed.</summary>
public sealed class FakeGraphStore : IGraphModuleStore<CalendarItem>
{
    private int _nextId = 1;
    public Dictionary<string, (CalendarItem Item, DateTime ModifiedUtc)> Rows { get; } = new();
    public List<GraphRecord<CalendarItem>> PendingChanges { get; } = new();
    public int Creates, Updates, Deletes;

    public string Seed(CalendarItem item, DateTime modifiedUtc, bool announce = true)
    {
        var id = $"G-{_nextId++}";
        Rows[id] = (item, modifiedUtc);
        if (announce) PendingChanges.Add(new GraphRecord<CalendarItem>(id, modifiedUtc, false, item));
        return id;
    }

    public void AnnounceDeleted(string id)
    {
        Rows.Remove(id);
        PendingChanges.Add(new GraphRecord<CalendarItem>(id, DateTime.UtcNow, true, null));
    }

    public Task<GraphDelta<CalendarItem>> GetChangesAsync(string mailbox, string? deltaToken, CancellationToken ct)
    {
        var changes = PendingChanges.ToList();
        PendingChanges.Clear();
        return Task.FromResult(new GraphDelta<CalendarItem>(changes, $"token-{Guid.NewGuid():N}"));
    }

    public Task<string> CreateAsync(string mailbox, CalendarItem item, CancellationToken ct)
    {
        Creates++;
        return Task.FromResult(Seed(item, DateTime.UtcNow, announce: false));
    }

    public Task UpdateAsync(string mailbox, string graphId, CalendarItem item, CancellationToken ct)
    {
        Updates++;
        Rows[graphId] = (item, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string mailbox, string graphId, CancellationToken ct)
    {
        Deletes++;
        Rows.Remove(graphId);
        return Task.CompletedTask;
    }
}

public static class TestData
{
    public static CalendarItem Event(string subject, DateTime startUtc) => new()
    {
        Subject = subject,
        StartUtc = startUtc,
        EndUtc = startUtc.AddHours(1),
        AllDay = false
    };

    public static string NaturalKey(CalendarItem i) => $"{i.Subject?.Trim().ToLowerInvariant()}|{i.StartUtc:yyyyMMddHHmm}";
}
