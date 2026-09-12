using System.Text.Json;

internal static class BoundedHttpJson
{
    internal const int MaximumProviderResponseBytes = 512 * 1024;
    internal const int MaximumStoreResponseBytes = 256 * 1024;
    internal const int MaximumEntraResponseBytes = 128 * 1024;
    internal static readonly TimeSpan ProviderBodyTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan StoreBodyTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan EntraBodyTimeout = TimeSpan.FromSeconds(10);

    internal static async Task<JsonDocument?> TryReadAsync(
        HttpContent content,
        int maximumBytes,
        int maximumDepth,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (maximumDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        using CancellationTokenSource bodyDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyDeadline.CancelAfter(timeout);
        CancellationToken bodyToken = bodyDeadline.Token;

        try
        {
            if (content.Headers.ContentLength is long declaredLength && declaredLength > maximumBytes)
                return null;

            await using Stream source = await content.ReadAsStreamAsync(bodyToken).ConfigureAwait(false);
            using MemoryStream buffer = new(capacity: Math.Min(maximumBytes, 64 * 1024));
            byte[] chunk = new byte[16 * 1024];
            int total = 0;

            while (true)
            {
                int read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), bodyToken).ConfigureAwait(false);
                if (read == 0) break;

                total = checked(total + read);
                if (total > maximumBytes)
                    return null;

                await buffer.WriteAsync(chunk.AsMemory(0, read), bodyToken).ConfigureAwait(false);
            }

            buffer.Position = 0;
            return await JsonDocument.ParseAsync(
                buffer,
                new JsonDocumentOptions { MaxDepth = maximumDepth },
                bodyToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
