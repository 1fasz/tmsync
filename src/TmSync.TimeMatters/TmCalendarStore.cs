using Microsoft.Data.SqlClient;
using System.Data;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.TimeMatters;

/// <summary>
/// Reads and writes Time Matters Events through the TmSync SQL contract
/// (view dbo.TmSync_Events and procs TmSync_CreateEvent / TmSync_UpdateEvent / TmSync_DeleteEvent).
/// See sql/TmSync_Contract_Template.sql.
/// </summary>
public sealed class TmCalendarStore : ITmModuleStore<CalendarItem>
{
    private readonly TmDb _db;

    public TmCalendarStore(TmDb db) => _db = db;

    public async Task<IReadOnlyList<TmRecord<CalendarItem>>> GetChangedSinceAsync(string staffCode, DateTime? sinceUtc, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TmId, Subject, StartUtc, EndUtc, AllDay, Location, Notes, ReminderMinutes, LastModifiedUtc, IsDeleted
            FROM dbo.TmSync_Events
            WHERE StaffCode = @staff AND (@since IS NULL OR LastModifiedUtc > @since)
            """;
        cmd.Parameters.AddWithValue("@staff", staffCode);
        cmd.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = sinceUtc.Db() });

        var results = new List<TmRecord<CalendarItem>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var deleted = reader.GetBoolean(9);
            var item = deleted ? null : new CalendarItem
            {
                Subject = reader.GetStringOrNull(1),
                StartUtc = reader.GetUtc(2),
                EndUtc = reader.GetUtc(3),
                AllDay = reader.GetBoolean(4),
                Location = reader.GetStringOrNull(5),
                BodyText = reader.GetStringOrNull(6),
                ReminderMinutes = reader.GetIntOrNull(7)
            };
            results.Add(new TmRecord<CalendarItem>(reader.GetString(0), reader.GetUtc(8), deleted, item));
        }
        return results;
    }

    public async Task<string> CreateAsync(string staffCode, CalendarItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_CreateEvent";
        AddItemParameters(cmd, staffCode, item);
        var outId = new SqlParameter("@TmId", SqlDbType.VarChar, 64) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(outId);
        await cmd.ExecuteNonQueryAsync(ct);
        return (string)outId.Value!;
    }

    public async Task UpdateAsync(string tmId, CalendarItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_UpdateEvent";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        AddItemParameters(cmd, staffCode: null, item);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string tmId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_DeleteEvent";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddItemParameters(SqlCommand cmd, string? staffCode, CalendarItem item)
    {
        if (staffCode != null) cmd.Parameters.AddWithValue("@StaffCode", staffCode);
        cmd.Parameters.AddWithValue("@Subject", item.Subject.Db());
        cmd.Parameters.Add(new SqlParameter("@StartUtc", SqlDbType.DateTime2) { Value = item.StartUtc });
        cmd.Parameters.Add(new SqlParameter("@EndUtc", SqlDbType.DateTime2) { Value = item.EndUtc });
        cmd.Parameters.AddWithValue("@AllDay", item.AllDay);
        cmd.Parameters.AddWithValue("@Location", item.Location.Db());
        cmd.Parameters.AddWithValue("@Notes", item.BodyText.Db());
        cmd.Parameters.AddWithValue("@ReminderMinutes", ((object?)item.ReminderMinutes).Db());
    }
}
