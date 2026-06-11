using Microsoft.Extensions.Logging;
using TmSync.Core.Abstractions;
using TmSync.Core.Config;
using TmSync.Core.Models;
using TmSync.Core.Sync;

namespace TmSync.Service;

/// <summary>Runs the enabled sync modules for every enabled user (or a filtered subset).</summary>
public sealed class SyncRunner
{
    private readonly IStateStore _state;
    private readonly TmSyncOptions _options;
    private readonly ITmModuleStore<CalendarItem> _tmCalendar;
    private readonly ITmModuleStore<ContactItem> _tmContacts;
    private readonly ITmModuleStore<TaskItem> _tmTasks;
    private readonly IGraphModuleStore<CalendarItem> _graphCalendar;
    private readonly IGraphModuleStore<ContactItem> _graphContacts;
    private readonly IGraphModuleStore<TaskItem> _graphTasks;
    private readonly IGraphMailReader _mailReader;
    private readonly ITmEmailSink _emailSink;
    private readonly ILogger<SyncRunner> _log;

    public SyncRunner(
        IStateStore state,
        TmSyncOptions options,
        ITmModuleStore<CalendarItem> tmCalendar,
        ITmModuleStore<ContactItem> tmContacts,
        ITmModuleStore<TaskItem> tmTasks,
        IGraphModuleStore<CalendarItem> graphCalendar,
        IGraphModuleStore<ContactItem> graphContacts,
        IGraphModuleStore<TaskItem> graphTasks,
        IGraphMailReader mailReader,
        ITmEmailSink emailSink,
        ILogger<SyncRunner> log)
    {
        _state = state;
        _options = options;
        _tmCalendar = tmCalendar;
        _tmContacts = tmContacts;
        _tmTasks = tmTasks;
        _graphCalendar = graphCalendar;
        _graphContacts = graphContacts;
        _graphTasks = graphTasks;
        _mailReader = mailReader;
        _emailSink = emailSink;
        _log = log;
    }

    public async Task RunOnceAsync(string? onlyStaffCode, SyncModule? onlyModule, CancellationToken ct)
    {
        var users = _state.ListUsers().Where(u => u.Enabled);
        if (onlyStaffCode != null)
            users = users.Where(u => string.Equals(u.StaffCode, onlyStaffCode, StringComparison.OrdinalIgnoreCase));

        var userList = users.ToList();
        if (userList.Count == 0)
        {
            _log.LogWarning("No enabled sync users configured. Add one with: tmsync users add <staffCode> <mailbox>");
            return;
        }

        var engineOptions = new SyncEngineOptions
        {
            ConflictPolicy = _options.ConflictPolicy,
            PropagateDeletes = _options.PropagateDeletes
        };

        foreach (var user in userList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (ShouldRun(SyncModule.Calendar, onlyModule, _options.Modules.Calendar, user.Calendar))
                {
                    var engine = new SyncEngine<CalendarItem>(SyncModule.Calendar, _tmCalendar, _graphCalendar, _state,
                        i => $"{i.Subject?.Trim().ToLowerInvariant()}|{i.StartUtc:yyyyMMddHHmm}", engineOptions, _log);
                    await engine.RunAsync(user, ct);
                }

                if (ShouldRun(SyncModule.Contacts, onlyModule, _options.Modules.Contacts, user.Contacts))
                {
                    var engine = new SyncEngine<ContactItem>(SyncModule.Contacts, _tmContacts, _graphContacts, _state,
                        i => !string.IsNullOrWhiteSpace(i.Email1)
                            ? i.Email1.Trim().ToLowerInvariant()
                            : $"{i.FirstName?.Trim().ToLowerInvariant()}|{i.LastName?.Trim().ToLowerInvariant()}|{i.Company?.Trim().ToLowerInvariant()}",
                        engineOptions, _log);
                    await engine.RunAsync(user, ct);
                }

                if (ShouldRun(SyncModule.Tasks, onlyModule, _options.Modules.Tasks, user.Tasks))
                {
                    var engine = new SyncEngine<TaskItem>(SyncModule.Tasks, _tmTasks, _graphTasks, _state,
                        i => $"{i.Subject?.Trim().ToLowerInvariant()}|{i.DueDateUtc:yyyyMMdd}", engineOptions, _log);
                    await engine.RunAsync(user, ct);
                }

                if (ShouldRun(SyncModule.EmailJournal, onlyModule, _options.Modules.EmailJournal, user.EmailJournal))
                {
                    var runner = new EmailJournalRunner(_mailReader, _emailSink, _state, _log);
                    await runner.RunAsync(user, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Sync failed for user {Staff} ({Mailbox})", user.StaffCode, user.Mailbox);
            }
        }
    }

    private static bool ShouldRun(SyncModule module, SyncModule? filter, bool globalToggle, bool userToggle) =>
        (filter is null || filter == module) && globalToggle && userToggle;
}
