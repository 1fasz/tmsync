using Microsoft.Extensions.Logging;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.Core.Sync;

/// <summary>
/// One-way pipeline that copies new Office 365 mail (Inbox and Sent Items) into Time Matters
/// email records. Deduplicated by internet message id.
/// </summary>
public sealed class EmailJournalRunner
{
    private static readonly (string Folder, string Direction)[] Folders =
    {
        ("inbox", "Incoming"),
        ("sentitems", "Outgoing")
    };

    private readonly IGraphMailReader _mail;
    private readonly ITmEmailSink _sink;
    private readonly IStateStore _state;
    private readonly ILogger _log;

    public EmailJournalRunner(IGraphMailReader mail, ITmEmailSink sink, IStateStore state, ILogger log)
    {
        _mail = mail;
        _sink = sink;
        _state = state;
        _log = log;
    }

    public async Task<int> RunAsync(SyncUser user, CancellationToken ct)
    {
        var journaled = 0;
        foreach (var (folder, direction) in Folders)
        {
            var moduleKey = $"{SyncModule.EmailJournal}:{folder}";
            var state = _state.GetModuleState(moduleKey, user.StaffCode);

            GraphDelta<JournalEmail> delta;
            try
            {
                delta = await _mail.GetNewMessagesAsync(user.Mailbox, folder, state.DeltaToken, ct);
            }
            catch (DeltaTokenExpiredException)
            {
                _log.LogWarning("EmailJournal/{Staff}: delta token for {Folder} expired, restarting from now", user.StaffCode, folder);
                delta = await _mail.GetNewMessagesAsync(user.Mailbox, folder, null, ct);

                // On a forced reset, skip the historical backlog: mark everything as seen without journaling.
                foreach (var rec in delta.Changes)
                {
                    var mid = rec.Item?.InternetMessageId;
                    if (!string.IsNullOrEmpty(mid)) _state.MarkJournaled(user.StaffCode, mid);
                }
                _state.SaveModuleState(moduleKey, user.StaffCode, delta.DeltaToken, null, DateTime.UtcNow);
                continue;
            }

            // The very first run establishes a baseline: existing mail is not back-filled into
            // Time Matters, only mail arriving after journaling was enabled.
            var firstRun = state.DeltaToken is null;

            foreach (var rec in delta.Changes)
            {
                ct.ThrowIfCancellationRequested();
                var email = rec.Item;
                if (email is null || string.IsNullOrEmpty(email.InternetMessageId)) continue;
                if (_state.IsJournaled(user.StaffCode, email.InternetMessageId)) continue;

                if (firstRun)
                {
                    _state.MarkJournaled(user.StaffCode, email.InternetMessageId);
                    continue;
                }

                try
                {
                    email.Direction = direction;
                    await _sink.SaveAsync(user.StaffCode, email, ct);
                    _state.MarkJournaled(user.StaffCode, email.InternetMessageId);
                    journaled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "EmailJournal/{Staff}: failed saving message {Id}", user.StaffCode, email.InternetMessageId);
                }
            }

            _state.SaveModuleState(moduleKey, user.StaffCode, delta.DeltaToken, null, DateTime.UtcNow);
        }

        if (journaled > 0)
            _log.LogInformation("EmailJournal/{Staff}: journaled {Count} message(s)", user.StaffCode, journaled);

        return journaled;
    }
}
