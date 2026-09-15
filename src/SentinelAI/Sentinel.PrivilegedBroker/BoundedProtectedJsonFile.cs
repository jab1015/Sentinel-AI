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

            if (stream.Length is <= 0 or > MaximumBytes)
                return false;

            using MemoryStream bounded = new(capacity: Math.Min((int)stream.Length, MaximumBytes));
            byte[] buffer = new byte[4096];
            int total = 0;
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total = checked(total + read);
                if (total > MaximumBytes)
                    return false;
                bounded.Write(buffer, 0, read);
            }

            if (total == 0)
                return false;

            bounded.Position = 0;
            value = JsonSerializer.Deserialize<T>(bounded, new JsonSerializerOptions { MaxDepth = 32 });
            return value is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or OverflowException)
        {
            value = default;
            return false;
        }
    }
}
