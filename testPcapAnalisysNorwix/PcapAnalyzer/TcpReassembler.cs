using System.Buffers;
using System.Buffers.Binary;

/// <summary>
/// Reassembles protocol frames from raw TCP byte streams.
/// Frame framing: STX(0x02) | FrameSize:uint32BE | 3-byte-header | payload | ETX(0x03)
/// Total frame length = 5 + FrameSize bytes.
/// </summary>
public sealed class TcpReassembler : IDisposable
{
    private readonly Dictionary<string, StreamBuffer> _streams = new();

    public delegate void FrameAction(ReadOnlySpan<byte> frame);

    public void AddData(string streamKey, ReadOnlySpan<byte> data, FrameAction onFrame)
    {
        if (!_streams.TryGetValue(streamKey, out var sb))
        {
            sb = new StreamBuffer();
            _streams[streamKey] = sb;
        }
        sb.Append(data);
        sb.ExtractFrames(onFrame);
    }

    public void Dispose()
    {
        foreach (var sb in _streams.Values) sb.Dispose();
        _streams.Clear();
    }

    private sealed class StreamBuffer : IDisposable
    {
        private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        private byte[] _buf;
        private int    _len;

        public StreamBuffer()
        {
            _buf = _pool.Rent(8192);
            _len = 0;
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_len + data.Length);
            data.CopyTo(_buf.AsSpan(_len));
            _len += data.Length;
        }

        public void ExtractFrames(FrameAction onFrame)
        {
            int pos = 0;

            while (pos + 5 <= _len)
            {
                // Re-sync: find the next STX
                int stx = _buf.AsSpan(pos, _len - pos).IndexOf((byte)0x02);
                if (stx < 0) { pos = _len; break; }
                pos += stx;

                if (pos + 5 > _len) break;

                uint frameSize = BinaryPrimitives.ReadUInt32BigEndian(_buf.AsSpan(pos + 1, 4));

                // Sanity-check: max ~2 MB per spec; min is 4 bytes (3-header + ETX)
                if (frameSize < 4 || frameSize > 2_100_000)
                {
                    pos++; // skip bad STX and re-sync
                    continue;
                }

                int totalLen = (int)(5 + frameSize);
                if (pos + totalLen > _len) break;

                if (_buf[pos + totalLen - 1] == 0x03) // validate ETX
                    onFrame(_buf.AsSpan(pos, totalLen));

                pos += totalLen;
            }

            // Compact: move remaining bytes to start of buffer
            if (pos > 0)
            {
                int remaining = _len - pos;
                if (remaining > 0)
                    _buf.AsSpan(pos, remaining).CopyTo(_buf.AsSpan());
                _len = remaining;
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (needed <= _buf.Length) return;
            int newSize = Math.Max(needed, _buf.Length * 2);
            var next = _pool.Rent(newSize);
            _buf.AsSpan(0, _len).CopyTo(next);
            _pool.Return(_buf);
            _buf = next;
        }

        public void Dispose()
        {
            _pool.Return(_buf);
            _buf = Array.Empty<byte>();
            _len = 0;
        }
    }
}
