using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;

// Sends an XML file to the printer using the same command 0xB2 frame this
// device speaks on port 10000 (STX | FrameSize:u32BE | 2-byte seq | cmd | payload | ETX),
// then waits for the 0x09 acknowledgement frame and prints the echoed command Id.
const byte XmlCommand = 0xB2;
const byte AckCommand = 0x09;
const int  DefaultPort = 10000;
const int  ResponseTimeoutSeconds = 10;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: NorwixInkDbSender <xmlFile> <deviceIp> [port=10000]");
    return 1;
}

string xmlFile  = args[0];
string deviceIp = args[1];
int    port     = args.Length > 2 ? int.Parse(args[2]) : DefaultPort;

if (!File.Exists(xmlFile))
{
    Console.Error.WriteLine($"File not found: {xmlFile}");
    return 1;
}

var pool = ArrayPool<byte>.Shared;

// Read file into a pooled buffer and drop a leading UTF-8 BOM if present —
// the device expects raw XML text, not the BOM marker.
byte[] fileBuf;
int    fileLen;
await using (var fs = File.OpenRead(xmlFile))
{
    fileLen = checked((int)fs.Length);
    fileBuf = pool.Rent(fileLen);
    await fs.ReadExactlyAsync(fileBuf.AsMemory(0, fileLen));
}

int bomLen = ComputeBomLength(fileBuf, fileLen);
Memory<byte> xmlBytes = fileBuf.AsMemory(bomLen, fileLen - bomLen);

// Frame = STX + FrameSize(u32 BE) + seq(2 bytes) + cmd + payload + ETX
// FrameSize counts everything after itself, i.e. seq + cmd + payload + ETX.
uint   frameSize = (uint)(2 + 1 + xmlBytes.Length + 1);
int    frameLen  = (int)(5 + frameSize);
byte[] frameBuf  = pool.Rent(frameLen);
try
{
    BuildFrame(frameBuf, frameLen, frameSize, XmlCommand, xmlBytes);

    Console.WriteLine($"Connecting to {deviceIp}:{port} ...");
    using var client = new TcpClient();
    await client.ConnectAsync(deviceIp, port);
    using var stream = client.GetStream();

    await stream.WriteAsync(frameBuf.AsMemory(0, frameLen));
    Console.WriteLine($"Sent command 0x{XmlCommand:X2} ({xmlBytes.Length} bytes XML).");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
    try
    {
        byte? commandId = await WaitForAckFrameAsync(stream, cts.Token);
        if (commandId is null)
        {
            Console.Error.WriteLine("Connection closed before an ack frame was received.");
            return 1;
        }

        Console.WriteLine($"Received ack 0x{AckCommand:X2} for command 0x{commandId:X2}.");
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine($"Timed out after {ResponseTimeoutSeconds}s waiting for ack.");
        return 1;
    }
}
finally
{
    pool.Return(fileBuf);
    pool.Return(frameBuf);
}

// Reads from the stream and parses frames (STX | size:u32BE | header | ETX), growing a
// pooled buffer as needed, until a 0x09 ack frame arrives; returns the echoed command Id.
static async Task<byte?> WaitForAckFrameAsync(NetworkStream stream, CancellationToken ct)
{
    var pool = ArrayPool<byte>.Shared;
    byte[] buf = pool.Rent(4096);
    int    len = 0;

    try
    {
        while (true)
        {
            int pos = 0;
            while (pos + 5 <= len)
            {
                int stx = buf.AsSpan(pos, len - pos).IndexOf((byte)0x02);
                if (stx < 0) { pos = len; break; }
                pos += stx;
                if (pos + 5 > len) break;

                uint frameSize = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(pos + 1, 4));
                if (frameSize < 4 || frameSize > 2_100_000) { pos++; continue; }

                int total = (int)(5 + frameSize);
                if (pos + total > len) break;

                if (buf[pos + total - 1] == 0x03 && buf[pos + 7] == AckCommand)
                    return buf[pos + 8];

                pos += total;
            }

            // Compact unconsumed bytes to the front before reading more.
            if (pos > 0)
            {
                buf.AsSpan(pos, len - pos).CopyTo(buf);
                len -= pos;
            }

            if (len == buf.Length)
            {
                var bigger = pool.Rent(buf.Length * 2);
                buf.AsSpan(0, len).CopyTo(bigger);
                pool.Return(buf);
                buf = bigger;
            }

            int n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return null; // connection closed
            len += n;
        }
    }
    finally
    {
        pool.Return(buf);
    }
}

// UTF-8 byte order mark, as a zero-allocation span backed by the compiled string literal.
static ReadOnlySpan<byte> Utf8Bom() => "\uFEFF"u8;

// Kept outside the async top-level Main: a Span local can't cross an await point,
// so the BOM check happens in this plain (non-async) function instead.
static int ComputeBomLength(byte[] data, int length)
{
    ReadOnlySpan<byte> bom  = Utf8Bom();
    ReadOnlySpan<byte> head = data.AsSpan(0, Math.Min(length, bom.Length));
    return MatchesBom(head, bom) ? bom.Length : 0;
}

static bool MatchesBom(ReadOnlySpan<byte> head, ReadOnlySpan<byte> bom) => head.SequenceEqual(bom);

// Fills the STX | size | seq | cmd | payload | ETX frame directly in the pooled buffer.
// Kept out of async Main: a Span local can't cross an await point.
static void BuildFrame(byte[] frameBuf, int frameLen, uint frameSize, byte cmd, ReadOnlyMemory<byte> payload)
{
    var frame = frameBuf.AsSpan(0, frameLen);
    frame[0] = 0x02;
    BinaryPrimitives.WriteUInt32BigEndian(frame.Slice(1, 4), frameSize);
    frame[5] = 0x00; // seq high byte
    frame[6] = 0x01; // seq low byte — device only needs to echo the command, not this counter
    frame[7] = cmd;
    payload.Span.CopyTo(frame.Slice(8));
    frame[^1] = 0x03;
}
