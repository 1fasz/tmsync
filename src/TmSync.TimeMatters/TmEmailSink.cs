using System.Data;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;

namespace TmSync.TimeMatters;

/// <summary>Saves journaled Office 365 email into Time Matters via proc dbo.TmSync_SaveEmail.</summary>
public sealed class TmEmailSink : ITmEmailSink
{
    private readonly TmDb _db;

    public TmEmailSink(TmDb db) => _db = db;

    public async Task SaveAsync(string staffCode, JournalEmail email, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "dbo.TmSync_SaveEmail";
        cmd.Parameters.AddWithValue("@StaffCode", staffCode);
        cmd.Parameters.AddWithValue("@Direction", email.Direction);
        cmd.Parameters.AddWithValue("@FromAddress", email.From.Db());
        cmd.Parameters.AddWithValue("@ToAddresses", email.To.Db());
        cmd.Parameters.AddWithValue("@CcAddresses", email.Cc.Db());
        cmd.Parameters.AddWithValue("@Subject", email.Subject.Db());
        cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@SentUtc", SqlDbType.DateTime2) { Value = email.SentUtc });
        cmd.Parameters.AddWithValue("@Body", email.BodyText.Db());
        cmd.Parameters.AddWithValue("@InternetMessageId", email.InternetMessageId.Db());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
