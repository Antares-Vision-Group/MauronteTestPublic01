using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

// Extracts the XML payload sent to the printer (device 172.25.0.22:10000)
// inside command 0xB2 frames. Frame layout is the shared STX/size/header/ETX
// framing already handled by TcpReassembler (from PcapAnalyzer); the XML text
// simply occupies bytes [8 .. length-2] of the frame (right after the
// command byte, up to the ETX terminator). Verified against both sample
// captures in this folder: no extra length/offset header, straight XML.
const string DeviceIp   = "172.25.0.22";
const int    DevicePort = 10000;
const byte   XmlCommand = 0xB2;

string pcapFile = args.Length > 0 ? args[0] : "OldDBcapture.pcap";
string outDir   = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();

if (!File.Exists(pcapFile))
{
    var candidate = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", pcapFile));
    if (File.Exists(candidate))
        pcapFile = candidate;
    else
    {
        Console.Error.WriteLine($"File not found: {pcapFile}");
        return 1;
    }
}

Console.WriteLine($"Analyzing: {Path.GetFullPath(pcapFile)}");

using var reassembler = new TcpReassembler();
var seenHashes = new HashSet<string>();
int extracted = 0;

using (var reader = new CaptureFileReaderDevice(pcapFile))
{
    reader.Open();

    while (reader.GetNextPacket(out PacketCapture capture) == GetPacketStatus.PacketRead)
    {
        var raw    = capture.GetPacket();
        var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);

        var tcpPacket = packet.Extract<TcpPacket>();
        if (tcpPacket is null) continue;

        var ip4 = packet.Extract<IPv4Packet>();
        if (ip4 is null) continue;

        bool isDeviceStream =
            (ip4.SourceAddress.ToString() == DeviceIp && tcpPacket.SourcePort == DevicePort) ||
            (ip4.DestinationAddress.ToString() == DeviceIp && tcpPacket.DestinationPort == DevicePort);
        if (!isDeviceStream) continue;

        var payload = tcpPacket.PayloadData;
        if (payload is null || payload.Length == 0) continue;

        var streamKey = $"{ip4.SourceAddress}:{tcpPacket.SourcePort}->{ip4.DestinationAddress}:{tcpPacket.DestinationPort}";

        reassembler.AddData(streamKey, payload, frame =>
        {
            if (frame.Length < 9 || frame[7] != XmlCommand) return;

            var xmlBytes = frame[8..^1]; // drop trailing ETX
            var xmlText  = Encoding.UTF8.GetString(xmlBytes);

            var hash = Convert.ToHexString(SHA256.HashData(xmlBytes));
            if (!seenHashes.Add(hash)) return; // duplicate content already captured

            extracted++;
            var outPath = Path.Combine(outDir,
                $"{Path.GetFileNameWithoutExtension(pcapFile)}_inkParameters_{extracted}.xml");
            File.WriteAllText(outPath, xmlText);
            Console.WriteLine($"  wrote {outPath} ({xmlBytes.Length} bytes)");
        });
    }
}

if (extracted == 0)
{
    Console.Error.WriteLine("No 0xB2 XML frame found for device 172.25.0.22:10000.");
    return 1;
}

Console.WriteLine($"Done. {extracted} distinct XML document(s) extracted.");
return 0;
