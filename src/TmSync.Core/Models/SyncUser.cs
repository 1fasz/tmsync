namespace TmSync.Core.Models;

/// <summary>Maps a Time Matters staff code to an Office 365 mailbox and controls which modules sync for them.</summary>
public sealed record SyncUser(
    string StaffCode,
    string Mailbox,
    bool Enabled = true,
    bool Calendar = true,
    bool Contacts = true,
    bool Tasks = true,
    bool EmailJournal = false);
