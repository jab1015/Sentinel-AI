using System.Runtime.CompilerServices;
using System.Text;

internal static class BrokerPipeMessageIOAcceptance
{
    [ModuleInitializer]
    internal static void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        byte[] valid = Encoding.UTF8.GetBytes("{\"Version\":2}\n");
        await using (MemoryStream stream = new(valid, writable: false))
        {
            string? line = await BrokerPipeMessageIO.ReadBoundedLineAsync(stream).ConfigureAwait(false);
            Require(line == "{\"Version\":2}", "A normal bounded broker request was not read exactly.");
        }

        byte[] oversized = Enumerable.Repeat((byte)'A', BrokerPipeMessageIO.MaximumMessageBytes + 1).Append((byte)'\n').ToArray();
        await using (MemoryStream stream = new(oversized, writable: false))
        {
            string? line = await BrokerPipeMessageIO.ReadBoundedLineAsync(stream).ConfigureAwait(false);
            Require(line is null, "An oversized broker request was accepted instead of failing closed.");
        }

        byte[] invalidUtf8 = { 0xC3, 0x28, (byte)'\n' };
        bool invalidEncodingRejected = false;
        try
        {
            await using MemoryStream stream = new(invalidUtf8, writable: false);
            _ = await BrokerPipeMessageIO.ReadBoundedLineAsync(stream).ConfigureAwait(false);
        }
        catch (DecoderFallbackException)
        {
            invalidEncodingRejected = true;
        }
        Require(invalidEncodingRejected, "Malformed UTF-8 was accepted by the privileged broker message boundary.");

        await using (MemoryStream stream = new())
        {
            await BrokerPipeMessageIO.WriteBoundedLineAsync(stream, "{\"Succeeded\":true}").ConfigureAwait(false);
            string written = Encoding.UTF8.GetString(stream.ToArray());
            Require(written == "{\"Succeeded\":true}\n", "A normal broker response was not framed exactly once.");
        }

        bool oversizedResponseRejected = false;
        try
        {
            await using MemoryStream stream = new();
            await BrokerPipeMessageIO.WriteBoundedLineAsync(stream, new string('R', BrokerPipeMessageIO.MaximumMessageBytes)).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            oversizedResponseRejected = true;
        }
        Require(oversizedResponseRejected, "An oversized broker response was accepted.");

        Console.WriteLine("Broker bounded request/response IO: PASS");
        Console.WriteLine("Broker oversized request rejected before unbounded line allocation: PASS");
        Console.WriteLine("Broker malformed UTF-8 rejected: PASS");
        Console.WriteLine("Broker oversized response rejected: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
