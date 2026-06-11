namespace TmSync.Core.Abstractions;

/// <summary>CRUD + delta access to one Microsoft 365 item type, scoped by mailbox.</summary>
public interface IGraphModuleStore<T> where T : class
{
    /// <summary>
    /// Returns changes since <paramref name="deltaToken"/>, or the full data set when the token is null.
    /// Throws <see cref="DeltaTokenExpiredException"/> when the token can no longer be used.
    /// </summary>
    Task<GraphDelta<T>> GetChangesAsync(string mailbox, string? deltaToken, CancellationToken ct);

    Task<string> CreateAsync(string mailbox, T item, CancellationToken ct);
    Task UpdateAsync(string mailbox, string graphId, T item, CancellationToken ct);
    Task DeleteAsync(string mailbox, string graphId, CancellationToken ct);
}
