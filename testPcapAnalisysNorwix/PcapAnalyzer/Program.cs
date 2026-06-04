using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

string pcapFile = args.Length > 0 ? args[0] : @"filtered mauro2.pcap";

if (!File.Exists(pcapFile))
{
    var candidate = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", pcapFile));
    if (File.Exists(candidate))
        pcapFile = candidate;
    else
    {
        Console.Error.WriteLine($"File not found: {pcapFile}");
        return 1;
    }
}

Console.WriteLine($"Analyzing: {Path.GetFullPath(pcapFile)}");
Console.WriteLine(new string('=', 70));

var reassembler = new TcpReassembler();
var analyzer    = new PrintAnalyzer();

using var reader = new CaptureFileReaderDevice(pcapFile);
reader.Open();

while (reader.GetNextPacket(out PacketCapture capture) == GetPacketStatus.PacketRead)
{
    var raw    = capture.GetPacket();
    var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);

    var tcpPacket = packet.Extract<TcpPacket>();
    if (tcpPacket is null) continue;

    var ip4 = packet.Extract<IPv4Packet>();
    if (ip4 is null) continue;

    int srcPort = tcpPacket.SourcePort;
    int dstPort = tcpPacket.DestinationPort;

    if (srcPort != 10000 && dstPort != 10000 &&
        srcPort != 10001 && dstPort != 10001)
        continue;

    var payload = tcpPacket.PayloadData;
    if (payload is null || payload.Length == 0) continue;

    var ts        = raw.Timeval.Date;
    var streamKey = $"{ip4.SourceAddress}:{srcPort}->{ip4.DestinationAddress}:{dstPort}";

    foreach (var frame in reassembler.AddData(streamKey, payload))
        analyzer.ProcessFrame(ts, frame, srcPort, dstPort, streamKey);
}

// Optional second arg: comma-separated known-duplicate serials to investigate
string[] knownDuplicates = args.Length > 1
    ? args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : Array.Empty<string>();

// Optional third arg: comma-separated serials with blank/unread OCR from camera
string[] blankOcrSerials = args.Length > 2
    ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : Array.Empty<string>();

analyzer.PrintReport(knownDuplicates, blankOcrSerials);
return 0;
