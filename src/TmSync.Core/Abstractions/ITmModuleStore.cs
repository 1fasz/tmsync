namespace TmSync.Core.Abstractions;

/// <summary>CRUD access to one Time Matters record type, scoped by staff code.</summary>
public interface ITmModuleStore<T> where T : class
{
    Task<IReadOnlyList<TmRecord<T>>> GetChangedSinceAsync(string staffCode, DateTime? sinceUtc, CancellationToken ct);
    Task<string> CreateAsync(string staffCode, T item, CancellationToken ct);
    Task UpdateAsync(string tmId, T item, CancellationToken ct);
    Task DeleteAsync(string tmId, CancellationToken ct);
}
