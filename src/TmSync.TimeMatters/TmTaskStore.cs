using Microsoft.Data.SqlClient;
using System.Data;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.TimeMatters;

/// <summary>
/// Reads and writes Time Matters ToDos through the TmSync SQL contract
/// (view dbo.TmSync_Todos and procs TmSync_CreateTodo / TmSync_UpdateTodo / TmSync_DeleteTodo).
/// </summary>
public sealed class TmTaskStore : ITmModuleStore<TaskItem>
{
    private readonly TmDb _db;

    public TmTaskStore(TmDb db) => _db = db;

    public async Task<IReadOnlyList<TmRecord<TaskItem>>> GetChangedSinceAsync(string staffCode, DateTime? sinceUtc, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TmId, Subject, DueDateUtc, Completed, CompletedUtc, Priority, Notes, LastModifiedUtc, IsDeleted
            FROM dbo.TmSync_Todos
            WHERE StaffCode = @staff AND (@since IS NULL OR LastModifiedUtc > @since)
            """;
        cmd.Parameters.AddWithValue("@staff", staffCode);
        cmd.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = sinceUtc.Db() });

        var results = new List<TmRecord<TaskItem>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var deleted = reader.GetBoolean(8);
            var item = deleted ? null : new TaskItem
            {
                Subject = reader.GetStringOrNull(1),
                DueDateUtc = reader.GetUtcOrNull(2),
                Completed = reader.GetBoolean(3),
                CompletedUtc = reader.GetUtcOrNull(4),
                Priority = reader.GetInt32(5),
                Notes = reader.GetStringOrNull(6)
            };
            results.Add(new TmRecord<TaskItem>(reader.GetString(0), reader.GetUtc(7), deleted, item));
        }
        return results;
    }

    public async Task<string> CreateAsync(string staffCode, TaskItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_CreateTodo";
        cmd.Parameters.AddWithValue("@StaffCode", staffCode);
        AddItemParameters(cmd, item);
        var outId = new SqlParameter("@TmId", SqlDbType.VarChar, 64) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(outId);
        await cmd.ExecuteNonQueryAsync(ct);
        return (string)outId.Value!;
    }

    public async Task UpdateAsync(string tmId, TaskItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_UpdateTodo";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        AddItemParameters(cmd, item);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string tmId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_DeleteTodo";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddItemParameters(SqlCommand cmd, TaskItem item)
    {
        cmd.Parameters.AddWithValue("@Subject", item.Subject.Db());
        cmd.Parameters.Add(new SqlParameter("@DueDateUtc", SqlDbType.DateTime2) { Value = ((object?)item.DueDateUtc).Db() });
        cmd.Parameters.AddWithValue("@Completed", item.Completed);
        cmd.Parameters.Add(new SqlParameter("@CompletedUtc", SqlDbType.DateTime2) { Value = ((object?)item.CompletedUtc).Db() });
        cmd.Parameters.AddWithValue("@Priority", item.Priority);
        cmd.Parameters.AddWithValue("@Notes", item.Notes.Db());
    }
}
