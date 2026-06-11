using System.Globalization;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using TmSync.Core.Abstractions;

namespace TmSync.Graph;

internal static class GraphHelpers
{
    /// <summary>True when a delta item carries the "@removed" annotation.</summary>
    public static bool IsRemoved(Entity entity) =>
        entity.AdditionalData?.ContainsKey("@removed") == true;

    /// <summary>Parses a Graph DateTimeTimeZone returned with Prefer: outlook.timezone="UTC".</summary>
    public static DateTime ParseUtc(DateTimeTimeZone? value, DateTime fallback = default)
    {
        if (value?.DateTime is null) return fallback;
        return DateTime.Parse(value.DateTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public static DateTime? ParseUtcOrNull(DateTimeTimeZone? value) =>
        value?.DateTime is null ? null : ParseUtc(value);

    public static DateTimeTimeZone ToGraphUtc(DateTime utc) => new()
    {
        DateTime = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        TimeZone = "UTC"
    };

    public static DateTime ToUtc(DateTimeOffset? value) => value?.UtcDateTime ?? DateTime.UtcNow;

    /// <summary>Translates Graph "delta token no longer valid" errors into DeltaTokenExpiredException.</summary>
    public static Exception Translate(ODataError error)
    {
        var code = error.Error?.Code;
        if (error.ResponseStatusCode == 410 ||
            string.Equals(code, "SyncStateNotFound", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "SyncStateInvalid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "resyncRequired", StringComparison.OrdinalIgnoreCase))
        {
            return new DeltaTokenExpiredException(error.Error?.Message ?? "Delta token expired", error);
        }
        return error;
    }
}
