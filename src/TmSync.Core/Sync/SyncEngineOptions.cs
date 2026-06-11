using TmSync.Core.Models;

namespace TmSync.Core.Sync;

public sealed class SyncEngineOptions
{
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.NewestWins;

    /// <summary>When true, deleting an item on one side deletes its counterpart on the other.</summary>
    public bool PropagateDeletes { get; set; } = true;
}
