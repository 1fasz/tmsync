using TmSync.Core.Models;

namespace TmSync.Core.Abstractions;

/// <summary>Reads new mail from an Office 365 mailbox folder via delta queries.</summary>
public interface IGraphMailReader
{
    Task<GraphDelta<JournalEmail>> GetNewMessagesAsync(string mailbox, string folder, string? deltaToken, CancellationToken ct);
}

/// <summary>Writes a journaled email into Time Matters.</summary>
public interface ITmEmailSink
{
    Task SaveAsync(string staffCode, JournalEmail email, CancellationToken ct);
}
