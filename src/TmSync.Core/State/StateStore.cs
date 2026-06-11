using Microsoft.Data.Sqlite;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.Core.State;

/// <summary>SQLite-backed implementation of <see cref="IStateStore"/>.</summary>
public sealed class StateStore : IStateStore
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    private readonly string _connectionString;

    public StateStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureCreated();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void EnsureCreated()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                StaffCode    TEXT NOT NULL PRIMARY KEY,
                Mailbox      TEXT NOT NULL,
                Enabled      INTEGER NOT NULL DEFAULT 1,
                Calendar     INTEGER NOT NULL DEFAULT 1,
                Contacts     INTEGER NOT NULL DEFAULT 1,
                Tasks        INTEGER NOT NULL DEFAULT 1,
                EmailJournal INTEGER NOT NULL DEFAULT 0,
                AddedUtc     TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Links (
                ModuleKey   TEXT NOT NULL,
                StaffCode   TEXT NOT NULL,
                TmId        TEXT NOT NULL,
                GraphId     TEXT NOT NULL,
                ContentHash TEXT NOT NULL,
                UpdatedUtc  TEXT NOT NULL,
                PRIMARY KEY (ModuleKey, StaffCode, TmId)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Links_Graph ON Links (ModuleKey, StaffCode, GraphId);
            CREATE TABLE IF NOT EXISTS ModuleState (
                ModuleKey      TEXT NOT NULL,
                StaffCode      TEXT NOT NULL,
                DeltaToken     TEXT NULL,
                TmWatermarkUtc TEXT NULL,
                LastRunUtc     TEXT NULL,
                PRIMARY KEY (ModuleKey, StaffCode)
            );
            CREATE TABLE IF NOT EXISTS JournaledMail (
                StaffCode         TEXT NOT NULL,
                InternetMessageId TEXT NOT NULL,
                JournaledUtc      TEXT NOT NULL,
                PRIMARY KEY (StaffCode, InternetMessageId)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static string FormatDate(DateTime utc) => utc.ToUniversalTime().ToString(DateFormat);

    private static DateTime? ParseDate(object? value) =>
        value is string s && s.Length > 0
            ? DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
            : null;

    // ---- Users ----

    public IReadOnlyList<SyncUser> ListUsers()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT StaffCode, Mailbox, Enabled, Calendar, Contacts, Tasks, EmailJournal FROM Users ORDER BY StaffCode";
        using var reader = cmd.ExecuteReader();
        var users = new List<SyncUser>();
        while (reader.Read())
        {
            users.Add(new SyncUser(
                reader.GetString(0), reader.GetString(1),
                reader.GetInt64(2) != 0, reader.GetInt64(3) != 0, reader.GetInt64(4) != 0,
                reader.GetInt64(5) != 0, reader.GetInt64(6) != 0));
        }
        return users;
    }

    public SyncUser? GetUser(string staffCode)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT StaffCode, Mailbox, Enabled, Calendar, Contacts, Tasks, EmailJournal FROM Users WHERE StaffCode = @s COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@s", staffCode);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new SyncUser(
            reader.GetString(0), reader.GetString(1),
            reader.GetInt64(2) != 0, reader.GetInt64(3) != 0, reader.GetInt64(4) != 0,
            reader.GetInt64(5) != 0, reader.GetInt64(6) != 0);
    }

    public void AddOrUpdateUser(SyncUser user)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Users (StaffCode, Mailbox, Enabled, Calendar, Contacts, Tasks, EmailJournal, AddedUtc)
            VALUES (@staff, @mailbox, @enabled, @cal, @con, @tasks, @mail, @added)
            ON CONFLICT(StaffCode) DO UPDATE SET
                Mailbox = excluded.Mailbox, Enabled = excluded.Enabled, Calendar = excluded.Calendar,
                Contacts = excluded.Contacts, Tasks = excluded.Tasks, EmailJournal = excluded.EmailJournal
            """;
        cmd.Parameters.AddWithValue("@staff", user.StaffCode);
        cmd.Parameters.AddWithValue("@mailbox", user.Mailbox);
        cmd.Parameters.AddWithValue("@enabled", user.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@cal", user.Calendar ? 1 : 0);
        cmd.Parameters.AddWithValue("@con", user.Contacts ? 1 : 0);
        cmd.Parameters.AddWithValue("@tasks", user.Tasks ? 1 : 0);
        cmd.Parameters.AddWithValue("@mail", user.EmailJournal ? 1 : 0);
        cmd.Parameters.AddWithValue("@added", FormatDate(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
    }

    public bool RemoveUser(string staffCode, bool purgeState)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM Users WHERE StaffCode = @s COLLATE NOCASE";
        del.Parameters.AddWithValue("@s", staffCode);
        var removed = del.ExecuteNonQuery() > 0;

        if (purgeState)
        {
            using var purge = conn.CreateCommand();
            purge.Transaction = tx;
            purge.CommandText = """
                DELETE FROM Links WHERE StaffCode = @s COLLATE NOCASE;
                DELETE FROM ModuleState WHERE StaffCode = @s COLLATE NOCASE;
                DELETE FROM JournaledMail WHERE StaffCode = @s COLLATE NOCASE;
                """;
            purge.Parameters.AddWithValue("@s", staffCode);
            purge.ExecuteNonQuery();
        }

        tx.Commit();
        return removed;
    }

    // ---- Links ----

    public SyncLink? FindLinkByTmId(string moduleKey, string staffCode, string tmId) =>
        FindLink("TmId", moduleKey, staffCode, tmId);

    public SyncLink? FindLinkByGraphId(string moduleKey, string staffCode, string graphId) =>
        FindLink("GraphId", moduleKey, staffCode, graphId);

    private SyncLink? FindLink(string column, string moduleKey, string staffCode, string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT ModuleKey, StaffCode, TmId, GraphId, ContentHash FROM Links WHERE ModuleKey = @m AND StaffCode = @s AND {column} = @id";
        cmd.Parameters.AddWithValue("@m", moduleKey);
        cmd.Parameters.AddWithValue("@s", staffCode);
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new SyncLink(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    public void UpsertLink(SyncLink link)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Links (ModuleKey, StaffCode, TmId, GraphId, ContentHash, UpdatedUtc)
            VALUES (@m, @s, @tm, @g, @h, @u)
            ON CONFLICT(ModuleKey, StaffCode, TmId) DO UPDATE SET
                GraphId = excluded.GraphId, ContentHash = excluded.ContentHash, UpdatedUtc = excluded.UpdatedUtc
            """;
        cmd.Parameters.AddWithValue("@m", link.ModuleKey);
        cmd.Parameters.AddWithValue("@s", link.StaffCode);
        cmd.Parameters.AddWithValue("@tm", link.TmId);
        cmd.Parameters.AddWithValue("@g", link.GraphId);
        cmd.Parameters.AddWithValue("@h", link.ContentHash);
        cmd.Parameters.AddWithValue("@u", FormatDate(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
    }

    public void RemoveLink(string moduleKey, string staffCode, string tmId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Links WHERE ModuleKey = @m AND StaffCode = @s AND TmId = @tm";
        cmd.Parameters.AddWithValue("@m", moduleKey);
        cmd.Parameters.AddWithValue("@s", staffCode);
        cmd.Parameters.AddWithValue("@tm", tmId);
        cmd.ExecuteNonQuery();
    }

    // ---- Module state ----

    public ModuleState GetModuleState(string moduleKey, string staffCode)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DeltaToken, TmWatermarkUtc, LastRunUtc FROM ModuleState WHERE ModuleKey = @m AND StaffCode = @s";
        cmd.Parameters.AddWithValue("@m", moduleKey);
        cmd.Parameters.AddWithValue("@s", staffCode);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return new ModuleState(null, null, null);
        return new ModuleState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            ParseDate(reader.IsDBNull(1) ? null : reader.GetString(1)),
            ParseDate(reader.IsDBNull(2) ? null : reader.GetString(2)));
    }

    public void SaveModuleState(string moduleKey, string staffCode, string? deltaToken, DateTime? tmWatermarkUtc, DateTime lastRunUtc)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ModuleState (ModuleKey, StaffCode, DeltaToken, TmWatermarkUtc, LastRunUtc)
            VALUES (@m, @s, @t, @w, @r)
            ON CONFLICT(ModuleKey, StaffCode) DO UPDATE SET
                DeltaToken = excluded.DeltaToken, TmWatermarkUtc = excluded.TmWatermarkUtc, LastRunUtc = excluded.LastRunUtc
            """;
        cmd.Parameters.AddWithValue("@m", moduleKey);
        cmd.Parameters.AddWithValue("@s", staffCode);
        cmd.Parameters.AddWithValue("@t", (object?)deltaToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", tmWatermarkUtc is null ? DBNull.Value : FormatDate(tmWatermarkUtc.Value));
        cmd.Parameters.AddWithValue("@r", FormatDate(lastRunUtc));
        cmd.ExecuteNonQuery();
    }

    // ---- Journaled mail ----

    public bool IsJournaled(string staffCode, string internetMessageId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM JournaledMail WHERE StaffCode = @s AND InternetMessageId = @id";
        cmd.Parameters.AddWithValue("@s", staffCode);
        cmd.Parameters.AddWithValue("@id", internetMessageId);
        return cmd.ExecuteScalar() != null;
    }

    public void MarkJournaled(string staffCode, string internetMessageId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO JournaledMail (StaffCode, InternetMessageId, JournaledUtc)
            VALUES (@s, @id, @u)
            ON CONFLICT(StaffCode, InternetMessageId) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@s", staffCode);
        cmd.Parameters.AddWithValue("@id", internetMessageId);
        cmd.Parameters.AddWithValue("@u", FormatDate(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
    }
}
