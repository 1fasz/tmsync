using Microsoft.Extensions.Logging;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.Core.Sync;

/// <summary>
/// Bidirectional sync between one Time Matters record type and its Microsoft 365 counterpart
/// for a single user. Incremental on both sides: a last-modified watermark on the Time Matters
/// side and a Graph delta token on the Microsoft 365 side. Content hashes stored on each link
/// suppress echoes of the engine's own writes and no-op updates.
/// </summary>
public sealed class SyncEngine<T> where T : class
{
    private readonly SyncModule _module;
    private readonly ITmModuleStore<T> _tm;
    private readonly IGraphModuleStore<T> _graph;
    private readonly IStateStore _state;
    private readonly Func<T, string> _naturalKey;
    private readonly SyncEngineOptions _options;
    private readonly ILogger _log;

    private string ModuleKey => _module.ToString();

    // One-way modes: when a side is not allowed to receive changes, the other side's
    // edits are ignored (they may be overwritten the next time the source record changes).
    private bool PushToGraph => _options.Direction != SyncDirection.M365ToTimeMatters;
    private bool PushToTm => _options.Direction != SyncDirection.TimeMattersToM365;

    public SyncEngine(
        SyncModule module,
        ITmModuleStore<T> tm,
        IGraphModuleStore<T> graph,
        IStateStore state,
        Func<T, string> naturalKey,
        SyncEngineOptions options,
        ILogger log)
    {
        _module = module;
        _tm = tm;
        _graph = graph;
        _state = state;
        _naturalKey = naturalKey;
        _options = options;
        _log = log;
    }

    public async Task<SyncStats> RunAsync(SyncUser user, CancellationToken ct)
    {
        var stats = new SyncStats();
        var state = _state.GetModuleState(ModuleKey, user.StaffCode);

        var pushToGraph = PushToGraph;
        var pushToTm = PushToTm;

        var tmChanges = await _tm.GetChangedSinceAsync(user.StaffCode, state.TmWatermarkUtc, ct);

        GraphDelta<T> delta;
        try
        {
            delta = await _graph.GetChangesAsync(user.Mailbox, state.DeltaToken, ct);
        }
        catch (DeltaTokenExpiredException)
        {
            _log.LogWarning("{Module}/{Staff}: delta token expired, performing full resync", ModuleKey, user.StaffCode);
            delta = await _graph.GetChangesAsync(user.Mailbox, null, ct);
        }

        // Classify Microsoft 365 changes against existing links.
        var graphCreates = new List<GraphRecord<T>>();
        var graphUpdates = new Dictionary<string, GraphRecord<T>>(); // keyed by linked TmId
        var graphDeletes = new List<SyncLink>();

        foreach (var g in delta.Changes)
        {
            var link = _state.FindLinkByGraphId(ModuleKey, user.StaffCode, g.GraphId);
            if (g.IsDeleted)
            {
                if (link != null) graphDeletes.Add(link);
                continue;
            }
            if (g.Item is null) continue;

            if (link is null)
            {
                graphCreates.Add(g);
            }
            else if (link.ContentHash != ContentHasher.Hash(g.Item))
            {
                graphUpdates[link.TmId] = g;
            }
        }

        // Classify Time Matters changes; advance the watermark to the newest change seen.
        var tmCreates = new List<TmRecord<T>>();
        var tmUpdates = new List<(TmRecord<T> Rec, SyncLink Link)>();
        var tmDeletes = new List<SyncLink>();
        var watermark = state.TmWatermarkUtc;

        foreach (var t in tmChanges)
        {
            if (watermark is null || t.LastModifiedUtc > watermark) watermark = t.LastModifiedUtc;

            var link = _state.FindLinkByTmId(ModuleKey, user.StaffCode, t.TmId);
            if (t.IsDeleted)
            {
                if (link != null) tmDeletes.Add(link);
                continue;
            }
            if (t.Item is null) continue;

            if (link is null)
            {
                tmCreates.Add(t);
            }
            else if (link.ContentHash != ContentHasher.Hash(t.Item))
            {
                tmUpdates.Add((t, link));
            }
        }

        // Pair unlinked items that already exist on both sides (e.g. data left behind by the old
        // Exchange sync) by natural key, so the first run links them instead of duplicating them.
        MatchByNaturalKey(user, tmCreates, graphCreates, stats);

        // Creations
        foreach (var g in graphCreates)
        {
            if (!pushToTm) continue;
            ct.ThrowIfCancellationRequested();
            try
            {
                var tmId = await _tm.CreateAsync(user.StaffCode, g.Item!, ct);
                _state.UpsertLink(new SyncLink(ModuleKey, user.StaffCode, tmId, g.GraphId, ContentHasher.Hash(g.Item!)));
                stats.CreatedInTm++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed creating Time Matters record from M365 item {Id}", ModuleKey, user.StaffCode, g.GraphId);
            }
        }

        foreach (var t in tmCreates)
        {
            if (!pushToGraph) continue;
            ct.ThrowIfCancellationRequested();
            try
            {
                var graphId = await _graph.CreateAsync(user.Mailbox, t.Item!, ct);
                _state.UpsertLink(new SyncLink(ModuleKey, user.StaffCode, t.TmId, graphId, ContentHasher.Hash(t.Item!)));
                stats.CreatedInGraph++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed creating M365 item from Time Matters record {Id}", ModuleKey, user.StaffCode, t.TmId);
            }
        }

        // Updates originating in Time Matters (resolving conflicts when both sides changed).
        foreach (var (t, link) in tmUpdates)
        {
            if (!pushToGraph) continue; // M365 is the source; the graphUpdates pass below handles any M365 edit
            ct.ThrowIfCancellationRequested();
            try
            {
                if (graphUpdates.TryGetValue(t.TmId, out var g))
                {
                    graphUpdates.Remove(t.TmId);
                    stats.Conflicts++;
                    if (!pushToTm || TimeMattersWinsConflict(t.LastModifiedUtc, g.LastModifiedUtc))
                    {
                        await _graph.UpdateAsync(user.Mailbox, link.GraphId, t.Item!, ct);
                        _state.UpsertLink(link with { ContentHash = ContentHasher.Hash(t.Item!) });
                        stats.UpdatedInGraph++;
                    }
                    else
                    {
                        await _tm.UpdateAsync(link.TmId, g.Item!, ct);
                        _state.UpsertLink(link with { ContentHash = ContentHasher.Hash(g.Item!) });
                        stats.UpdatedInTm++;
                    }
                }
                else
                {
                    await _graph.UpdateAsync(user.Mailbox, link.GraphId, t.Item!, ct);
                    _state.UpsertLink(link with { ContentHash = ContentHasher.Hash(t.Item!) });
                    stats.UpdatedInGraph++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed propagating update for Time Matters record {Id}", ModuleKey, user.StaffCode, t.TmId);
            }
        }

        // Remaining updates originating in Microsoft 365.
        foreach (var (tmId, g) in graphUpdates)
        {
            if (!pushToTm) continue;
            ct.ThrowIfCancellationRequested();
            try
            {
                var link = _state.FindLinkByTmId(ModuleKey, user.StaffCode, tmId);
                if (link is null) continue;
                await _tm.UpdateAsync(tmId, g.Item!, ct);
                _state.UpsertLink(link with { ContentHash = ContentHasher.Hash(g.Item!) });
                stats.UpdatedInTm++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed updating Time Matters record {Id}", ModuleKey, user.StaffCode, tmId);
            }
        }

        // Deletions
        foreach (var link in graphDeletes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (_options.PropagateDeletes && pushToTm)
                {
                    await _tm.DeleteAsync(link.TmId, ct);
                    stats.DeletedInTm++;
                }
                _state.RemoveLink(ModuleKey, user.StaffCode, link.TmId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed deleting Time Matters record {Id}", ModuleKey, user.StaffCode, link.TmId);
            }
        }

        foreach (var link in tmDeletes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (_options.PropagateDeletes && pushToGraph)
                {
                    await _graph.DeleteAsync(user.Mailbox, link.GraphId, ct);
                    stats.DeletedInGraph++;
                }
                _state.RemoveLink(ModuleKey, user.StaffCode, link.TmId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed deleting M365 item {Id}", ModuleKey, user.StaffCode, link.GraphId);
            }
        }

        _state.SaveModuleState(ModuleKey, user.StaffCode, delta.DeltaToken, watermark, DateTime.UtcNow);

        if (stats.HasActivity)
            _log.LogInformation("{Module}/{Staff}: {Stats}", ModuleKey, user.StaffCode, stats);

        return stats;
    }

    private void MatchByNaturalKey(SyncUser user, List<TmRecord<T>> tmCreates, List<GraphRecord<T>> graphCreates, SyncStats stats)
    {
        if (tmCreates.Count == 0 || graphCreates.Count == 0) return;

        var graphByKey = new Dictionary<string, GraphRecord<T>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in graphCreates)
        {
            var key = _naturalKey(g.Item!);
            if (!string.IsNullOrWhiteSpace(key)) graphByKey.TryAdd(key, g);
        }

        for (var i = tmCreates.Count - 1; i >= 0; i--)
        {
            var t = tmCreates[i];
            var key = _naturalKey(t.Item!);
            if (string.IsNullOrWhiteSpace(key) || !graphByKey.TryGetValue(key, out var g)) continue;

            graphByKey.Remove(key);
            graphCreates.Remove(g);
            tmCreates.RemoveAt(i);
            stats.Matched++;

            var tmHash = ContentHasher.Hash(t.Item!);
            var graphHash = ContentHasher.Hash(g.Item!);
            var winnerHash = tmHash;

            try
            {
                if (tmHash != graphHash)
                {
                    // Same natural key but different content: converge using the sync
                    // direction (the source side wins one-way) or the conflict policy.
                    if (!PushToTm || (PushToGraph && TimeMattersWinsConflict(t.LastModifiedUtc, g.LastModifiedUtc)))
                    {
                        _graph.UpdateAsync(user.Mailbox, g.GraphId, t.Item!, CancellationToken.None).GetAwaiter().GetResult();
                        stats.UpdatedInGraph++;
                    }
                    else
                    {
                        _tm.UpdateAsync(t.TmId, g.Item!, CancellationToken.None).GetAwaiter().GetResult();
                        winnerHash = graphHash;
                        stats.UpdatedInTm++;
                    }
                }
                _state.UpsertLink(new SyncLink(ModuleKey, user.StaffCode, t.TmId, g.GraphId, winnerHash));
            }
            catch (Exception ex)
            {
                stats.Errors++;
                _log.LogError(ex, "{Module}/{Staff}: failed linking matched pair {TmId}/{GraphId}", ModuleKey, user.StaffCode, t.TmId, g.GraphId);
            }
        }
    }

    private bool TimeMattersWinsConflict(DateTime tmModifiedUtc, DateTime graphModifiedUtc) =>
        _options.ConflictPolicy switch
        {
            ConflictPolicy.TimeMattersWins => true,
            ConflictPolicy.Microsoft365Wins => false,
            _ => tmModifiedUtc >= graphModifiedUtc
        };
}
