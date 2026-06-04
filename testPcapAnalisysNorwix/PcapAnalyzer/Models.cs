// ── Frame layout (all offsets 0-based) ─────────────────────────────────────
// [0]      STX  = 0x02
// [1..4]   FrameSize  (uint32 BE) — counts bytes from [5] to ETX inclusive
// [5..6]   reserved header bytes
// [7]      Message ID
// [8..]    payload
// [last]   ETX = 0x03
//
// Command 0x55  (port 10000, client→printer)
// [7]     0x55
// [8]     BufferNumber (0..31)
// [9..12] PageNumber   (uint32 BE, always 0)
// [13]    Pulse1Flag
// [14]    Pulse2Flag
// [15..18] DataBlockLength (uint32 BE)
// [19..]  DataBlock: CR+LF-delimited fields, terminated by FormFeed (0x0C)
//          Field index 12 (0-based) = SerialNumber
//
// Notification 0x06  (port 10001, printer→client)
// [7]      0x06
// [8]      BufferNumber (0..31) – buffer freed
// [9]      QueueCount   – buffers still waiting to print
// [10..13] PageNumber   (uint32 BE, always 0)
// [14..17] DropsP1  (uint32 BE) cumulative
// [18..21] DropsP2
// [22..25] DropsP3
// [26..29] DropsP4
// [30..33] EncoderPulses (uint32 BE) cumulative
// [34]     IsRealPrint   (0 = buffer-fill init, 1 = real print)
// [35]     PenStatus[0]  (0=ok, 1=not-printed, 2=partial)
// [36]     PenStatus[1]
// [37]     PenStatus[2]
// [38]     PenStatus[3]
// [39]     OverallStatus (0=ok, 1=partial-on-some-pens, 2=sync-lost)
// ────────────────────────────────────────────────────────────────────────────

public class Command55
{
    public DateTime Timestamp   { get; init; }
    public string   StreamKey   { get; init; } = "";
    public int      SeqIndex    { get; set; }       // global order in pcap

    public byte   BufferNumber  { get; init; }
    public uint   PageNumber    { get; init; }
    public byte   Pulse1Flag    { get; init; }
    public byte   Pulse2Flag    { get; init; }
    public uint   DataBlockLen  { get; init; }
    public string[] Fields      { get; init; } = Array.Empty<string>();

    public string SerialNumber =>
        Fields.Length > 12 ? Fields[12].Trim() : "(missing)";

    public static Command55? TryParse(DateTime ts, string key, byte[] f)
    {
        try
        {
            if (f.Length < 19 || f[7] != 0x55) return null;

            byte   buf  = f[8];
            uint   page = FrameHelper.Rd32(f, 9);
            byte   p1   = f[13];
            byte   p2   = f[14];
            uint   dlen = FrameHelper.Rd32(f, 15);

            if (f.Length < 19 + dlen) return null;

            var block  = System.Text.Encoding.ASCII.GetString(f, 19, (int)dlen);
            var fields = block.Split(new[] { "\r\n" }, StringSplitOptions.None);

            return new Command55
            {
                Timestamp  = ts,
                StreamKey  = key,
                BufferNumber = buf,
                PageNumber   = page,
                Pulse1Flag   = p1,
                Pulse2Flag   = p2,
                DataBlockLen = dlen,
                Fields       = fields
            };
        }
        catch { return null; }
    }
}

public class Notification06
{
    public DateTime Timestamp   { get; init; }
    public string   StreamKey   { get; init; } = "";
    public int      SeqIndex    { get; set; }

    public byte   BufferNumber  { get; init; }
    public byte   QueueCount    { get; init; }
    public uint   PageNumber    { get; init; }
    public uint[] DropsPerPen   { get; init; } = new uint[4];
    public uint   EncoderPulses { get; init; }
    public bool   IsRealPrint   { get; init; }
    public byte[] PenStatus     { get; init; } = new byte[4];
    public byte   OverallStatus { get; init; }

    public uint TotalDrops => DropsPerPen[0] + DropsPerPen[1] +
                              DropsPerPen[2] + DropsPerPen[3];

    public static Notification06? TryParse(DateTime ts, string key, byte[] f)
    {
        try
        {
            if (f.Length < 41 || f[7] != 0x06) return null;

            var drops = new uint[4];
            for (int i = 0; i < 4; i++)
                drops[i] = FrameHelper.Rd32(f, 14 + i * 4);

            return new Notification06
            {
                Timestamp     = ts,
                StreamKey     = key,
                BufferNumber  = f[8],
                QueueCount    = f[9],
                PageNumber    = FrameHelper.Rd32(f, 10),
                DropsPerPen   = drops,
                EncoderPulses = FrameHelper.Rd32(f, 30),
                IsRealPrint   = f[34] != 0,
                PenStatus     = new[] { f[35], f[36], f[37], f[38] },
                OverallStatus = f[39]
            };
        }
        catch { return null; }
    }
}

// Shared frame parsing helper
static class FrameHelper
{
    public static uint Rd32(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) |
        ((uint)b[o + 2] << 8) | b[o + 3];
}
