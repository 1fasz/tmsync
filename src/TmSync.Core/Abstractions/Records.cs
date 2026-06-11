namespace TmSync.Core.Abstractions;

/// <summary>A changed record on the Time Matters side.</summary>
public sealed record TmRecord<T>(string TmId, DateTime LastModifiedUtc, bool IsDeleted, T? Item) where T : class;

/// <summary>A changed record on the Microsoft 365 side.</summary>
public sealed record GraphRecord<T>(string GraphId, DateTime LastModifiedUtc, bool IsDeleted, T? Item) where T : class;

/// <summary>A page-complete set of changes from a Microsoft Graph delta query.</summary>
public sealed record GraphDelta<T>(IReadOnlyList<GraphRecord<T>> Changes, string? DeltaToken) where T : class;

/// <summary>Link between a Time Matters record and its Microsoft 365 counterpart.</summary>
public sealed record SyncLink(string ModuleKey, string StaffCode, string TmId, string GraphId, string ContentHash);

/// <summary>Persisted per-user, per-module incremental sync state.</summary>
public sealed record ModuleState(string? DeltaToken, DateTime? TmWatermarkUtc, DateTime? LastRunUtc);

/// <summary>Thrown by Graph stores when a stored delta token is no longer valid and a full resync is required.</summary>
public sealed class DeltaTokenExpiredException : Exception
{
    public DeltaTokenExpiredException(string message, Exception? inner = null) : base(message, inner) { }
}
