using System.Text.Json;

internal static class BoundedProtectedJsonFile
{
    internal const int MaximumBytes = 64 * 1024;

    internal static bool TryRead<T>(string path, out T? value)
    {
        value = default;
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            if (stream.Length is < 0 or > MaximumBytes)
                return false;

            value = JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { MaxDepth = 32 });
            return value is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            value = default;
            return false;
        }
    }
}
