using Microsoft.Data.SqlClient;
using System.Data;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.TimeMatters;

/// <summary>
/// Reads and writes Time Matters Contacts through the TmSync SQL contract
/// (view dbo.TmSync_Contacts and procs TmSync_CreateContact / TmSync_UpdateContact / TmSync_DeleteContact).
/// </summary>
public sealed class TmContactStore : ITmModuleStore<ContactItem>
{
    private readonly TmDb _db;

    public TmContactStore(TmDb db) => _db = db;

    public async Task<IReadOnlyList<TmRecord<ContactItem>>> GetChangedSinceAsync(string staffCode, DateTime? sinceUtc, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TmId, FirstName, LastName, Company, JobTitle, Email1, Email2,
                   BusinessPhone, MobilePhone, HomePhone, Street, City, State, PostalCode, Country,
                   Notes, LastModifiedUtc, IsDeleted
            FROM dbo.TmSync_Contacts
            WHERE StaffCode = @staff AND (@since IS NULL OR LastModifiedUtc > @since)
            """;
        cmd.Parameters.AddWithValue("@staff", staffCode);
        cmd.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = sinceUtc.Db() });

        var results = new List<TmRecord<ContactItem>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var deleted = reader.GetBoolean(17);
            var item = deleted ? null : new ContactItem
            {
                FirstName = reader.GetStringOrNull(1),
                LastName = reader.GetStringOrNull(2),
                Company = reader.GetStringOrNull(3),
                JobTitle = reader.GetStringOrNull(4),
                Email1 = reader.GetStringOrNull(5),
                Email2 = reader.GetStringOrNull(6),
                BusinessPhone = reader.GetStringOrNull(7),
                MobilePhone = reader.GetStringOrNull(8),
                HomePhone = reader.GetStringOrNull(9),
                Street = reader.GetStringOrNull(10),
                City = reader.GetStringOrNull(11),
                State = reader.GetStringOrNull(12),
                PostalCode = reader.GetStringOrNull(13),
                Country = reader.GetStringOrNull(14),
                Notes = reader.GetStringOrNull(15)
            };
            results.Add(new TmRecord<ContactItem>(reader.GetString(0), reader.GetUtc(16), deleted, item));
        }
        return results;
    }

    public async Task<string> CreateAsync(string staffCode, ContactItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_CreateContact";
        cmd.Parameters.AddWithValue("@StaffCode", staffCode);
        AddItemParameters(cmd, item);
        var outId = new SqlParameter("@TmId", SqlDbType.VarChar, 64) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(outId);
        await cmd.ExecuteNonQueryAsync(ct);
        return (string)outId.Value!;
    }

    public async Task UpdateAsync(string tmId, ContactItem item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_UpdateContact";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        AddItemParameters(cmd, item);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string tmId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_DeleteContact";
        cmd.Parameters.AddWithValue("@TmId", tmId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddItemParameters(SqlCommand cmd, ContactItem item)
    {
        cmd.Parameters.AddWithValue("@FirstName", item.FirstName.Db());
        cmd.Parameters.AddWithValue("@LastName", item.LastName.Db());
        cmd.Parameters.AddWithValue("@Company", item.Company.Db());
        cmd.Parameters.AddWithValue("@JobTitle", item.JobTitle.Db());
        cmd.Parameters.AddWithValue("@Email1", item.Email1.Db());
        cmd.Parameters.AddWithValue("@Email2", item.Email2.Db());
        cmd.Parameters.AddWithValue("@BusinessPhone", item.BusinessPhone.Db());
        cmd.Parameters.AddWithValue("@MobilePhone", item.MobilePhone.Db());
        cmd.Parameters.AddWithValue("@HomePhone", item.HomePhone.Db());
        cmd.Parameters.AddWithValue("@Street", item.Street.Db());
        cmd.Parameters.AddWithValue("@City", item.City.Db());
        cmd.Parameters.AddWithValue("@State", item.State.Db());
        cmd.Parameters.AddWithValue("@PostalCode", item.PostalCode.Db());
        cmd.Parameters.AddWithValue("@Country", item.Country.Db());
        cmd.Parameters.AddWithValue("@Notes", item.Notes.Db());
    }
}
