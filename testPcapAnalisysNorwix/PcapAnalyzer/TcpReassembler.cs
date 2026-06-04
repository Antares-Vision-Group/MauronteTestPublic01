/// <summary>
/// Reassembles protocol frames from raw TCP byte streams.
/// Frame framing: STX(0x02) | FrameSize:uint32BE | 3-byte-header | payload | ETX(0x03)
/// Total frame length = 5 + FrameSize bytes.
/// </summary>
public class TcpReassembler
{
    private readonly Dictionary<string, List<byte>> _buffers = new();

    public IEnumerable<byte[]> AddData(string streamKey, byte[] data)
    {
        if (!_buffers.TryGetValue(streamKey, out var buf))
        {
            buf = new List<byte>(data.Length * 2);
            _buffers[streamKey] = buf;
        }
        buf.AddRange(data);
        return ExtractFrames(buf);
    }

    private static List<byte[]> ExtractFrames(List<byte> buf)
    {
        var frames = new List<byte[]>();

        while (buf.Count >= 5)
        {
            // Re-sync: find the next STX
            int stx = buf.IndexOf(0x02);
            if (stx < 0) { buf.Clear(); break; }
            if (stx > 0) buf.RemoveRange(0, stx);
            if (buf.Count < 5) break;

            uint frameSize = ((uint)buf[1] << 24) | ((uint)buf[2] << 16) |
                             ((uint)buf[3] << 8)  |  (uint)buf[4];

            // Sanity-check: max ~2 MB per spec; min is 4 bytes (3-header + ETX)
            if (frameSize < 4 || frameSize > 2_100_000)
            {
                buf.RemoveAt(0); // skip STX and re-sync
                continue;
            }

            uint totalLen = 5 + frameSize;
            if (buf.Count < totalLen) break;

            var frame = buf.GetRange(0, (int)totalLen).ToArray();
            buf.RemoveRange(0, (int)totalLen);

            if (frame[^1] == 0x03) // validate ETX
                frames.Add(frame);
        }

        return frames;
    }
}
