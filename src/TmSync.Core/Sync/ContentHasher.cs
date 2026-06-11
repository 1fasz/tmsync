using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TmSync.Core.Sync;

/// <summary>
/// Produces a stable hash of an item's canonical model so the engine can detect real changes
/// and suppress echoes of its own writes coming back through delta queries.
/// </summary>
public static class ContentHasher
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Hash<T>(T item)
    {
        var json = JsonSerializer.Serialize(item, Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
