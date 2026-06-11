using Microsoft.Data.SqlClient;

namespace TmSync.TimeMatters;

/// <summary>Connection factory for the Time Matters SQL Server database.</summary>
public sealed class TmDb
{
    private readonly string _connectionString;

    public TmDb(string connectionString) => _connectionString = connectionString;

    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

internal static class SqlHelpers
{
    public static object Db(this object? value) => value ?? DBNull.Value;

    public static string? GetStringOrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static DateTime? GetUtcOrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    public static DateTime GetUtc(this SqlDataReader reader, int ordinal) =>
        DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    public static int? GetIntOrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}
