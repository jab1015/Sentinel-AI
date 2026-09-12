using System.Text;

internal static class BrokerPipeMessageIO
{
    internal const int MaximumMessageBytes = 32 * 1024;
    private const int ReadBufferCharacters = 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static async Task<string?> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadTimeout);

        Decoder decoder = StrictUtf8.GetDecoder();
        byte[] byteBuffer = new byte[1024];
        char[] charBuffer = new char[ReadBufferCharacters];
        StringBuilder text = new(capacity: Math.Min(4096, MaximumMessageBytes));
        int observedBytes = 0;
        bool sawCarriageReturn = false;

        while (true)
        {
            int read = await stream.ReadAsync(byteBuffer.AsMemory(0, byteBuffer.Length), deadline.Token).ConfigureAwait(false);
            if (read == 0)
            {
                if (text.Length == 0 && !sawCarriageReturn) return null;
                break;
            }

            observedBytes = checked(observedBytes + read);
            if (observedBytes > MaximumMessageBytes)
                return null;

            int byteOffset = 0;
            while (byteOffset < read)
            {
                decoder.Convert(
                    byteBuffer,
                    byteOffset,
                    read - byteOffset,
                    charBuffer,
                    0,
                    charBuffer.Length,
                    flush: false,
                    out int bytesUsed,
                    out int charsUsed,
                    out _);

                byteOffset += bytesUsed;
                for (int i = 0; i < charsUsed; i++)
                {
                    char value = charBuffer[i];
                    if (sawCarriageReturn)
                    {
                        if (value == '\n')
                            return text.Length == 0 ? null : text.ToString();

                        text.Append('\r');
                        sawCarriageReturn = false;
                    }

                    if (value == '\r')
                    {
                        sawCarriageReturn = true;
                        continue;
                    }

                    if (value == '\n')
                        return text.Length == 0 ? null : text.ToString();

                    text.Append(value);
                }
            }
        }

        if (sawCarriageReturn)
            text.Append('\r');

        decoder.Convert(
            Array.Empty<byte>(),
            0,
            0,
            charBuffer,
            0,
            charBuffer.Length,
            flush: true,
            out _,
            out int finalChars,
            out _);
        if (finalChars > 0)
            text.Append(charBuffer, 0, finalChars);

        return text.Length == 0 ? null : text.ToString();
    }

    internal static async Task WriteBoundedLineAsync(Stream stream, string message, CancellationToken cancellationToken = default)
    {
        int messageBytes = StrictUtf8.GetByteCount(message);
        if (messageBytes >= MaximumMessageBytes)
            throw new InvalidDataException("Broker response exceeded the maximum message size.");

        byte[] payload = new byte[messageBytes + 1];
        StrictUtf8.GetBytes(message.AsSpan(), payload.AsSpan(0, messageBytes));
        payload[messageBytes] = (byte)'\n';

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(WriteTimeout);
        await stream.WriteAsync(payload.AsMemory(0, payload.Length), deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
    }
}