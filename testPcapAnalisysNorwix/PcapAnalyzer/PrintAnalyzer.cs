public class PrintAnalyzer
{
    private readonly List<Command55>      _cmds   = new();
    private readonly List<Notification06> _notifs = new();
    private int _frameCount;

    public void ProcessFrame(DateTime ts, byte[] frame, int srcPort, int dstPort,
                             string streamKey)
    {
        if (frame.Length < 8) return;
        _frameCount++;

        byte id = frame[7];

        if (id == 0x55)
        {
            var cmd = Command55.TryParse(ts, streamKey, frame);
            if (cmd != null)
            {
                cmd.SeqIndex = _cmds.Count;
                _cmds.Add(cmd);
            }
        }
        else if (id == 0x06)
        {
            var notif = Notification06.TryParse(ts, streamKey, frame);
            if (notif != null)
            {
                notif.SeqIndex = _notifs.Count;
                _notifs.Add(notif);
            }
        }
    }

    public void PrintReport(string[] knownDuplicates, string[] blankOcrSerials)
    {
        // ── 1. Connection analysis – identify production vs test ─────────────
        Section("CONNECTION ANALYSIS");
        var productionWindow = AnalyzeConnections(out var productionStreams);

        // ── 2. Filter to production time window ──────────────────────────────
        var prodCmds = _cmds
            .Where(c => productionWindow.Contains(c.Timestamp) &&
                        productionStreams.Contains(NormKey(c.StreamKey)))
            .OrderBy(c => c.Timestamp)
            .ToList();

        var allRealPrints = _notifs
            .Where(n => n.IsRealPrint &&
                        productionWindow.Contains(n.Timestamp))
            .OrderBy(n => n.Timestamp)
            .ToList();

        Section("SUMMARY  (production connection only)");
        Console.WriteLine($"  Total frames parsed               : {_frameCount}");
        Console.WriteLine($"  Production 0x55 commands          : {prodCmds.Count}");
        Console.WriteLine($"  Production real prints (0x06)     : {allRealPrints.Count}");
        Console.WriteLine($"  Init buffer-fills (0x06 flag=0)   : " +
                          $"{_notifs.Count(n => !n.IsRealPrint && productionWindow.Contains(n.Timestamp))}");

        // ── 3. Buffer management ─────────────────────────────────────────────
        Section("BUFFER MANAGEMENT");
        AnalyzeBufferFlow(prodCmds, allRealPrints);

        // ── 4. Serial-number duplicate check ────────────────────────────────
        Section("SERIAL NUMBERS");
        AnalyzeSerialNumbers(prodCmds);

        // ── 5. Full-signature duplicate scan (standalone, no external input) ─
        Section("DUPLICATE SCAN  (full P1+P2 signature — same-buffer and cross-buffer)");
        ScanForDuplicates(prodCmds, allRealPrints);

        // ── 6. Known-duplicate investigation (optional) ───────────────────────
        if (knownDuplicates.Length > 0)
        {
            Section($"KNOWN-DUPLICATE INVESTIGATION  ({knownDuplicates.Length} serials)");
            InvestigateKnownDuplicates(prodCmds, allRealPrints, knownDuplicates);
        }

        // ── 7. Blank OCR analysis (optional) ─────────────────────────────────
        if (blankOcrSerials.Length > 0)
        {
            Section($"BLANK OCR / NO TEXT READ  ({blankOcrSerials.Length} serials)");
            AnalyzeBlankOcr(prodCmds, allRealPrints, blankOcrSerials);
        }

        // ── 8. Anomaly summary ───────────────────────────────────────────────
        Section("ANOMALY SUMMARY");
        PrintAnomalySummary(prodCmds, allRealPrints);
    }

    // ── Connection analysis ──────────────────────────────────────────────────

    /// <summary>
    /// Returns a time-range covering only the production connections,
    /// and fills <paramref name="productionStreamKeys"/> with the normalised
    /// source-side keys (IP:port pair of the client) for those connections.
    /// A connection is "test" when ≥ 90 % of its serial numbers are identical.
    /// </summary>
    private TimeRange AnalyzeConnections(out HashSet<string> productionStreamKeys)
    {
        // Group 0x55 commands by the normalised connection key (client IP:port → printer)
        var byConn = _cmds
            .GroupBy(c => NormKey(c.StreamKey))
            .ToList();

        productionStreamKeys = new HashSet<string>();
        DateTime prodStart = DateTime.MaxValue, prodEnd = DateTime.MinValue;

        Console.WriteLine($"  {"Connection",-45} {"Cmds",6} {"Distinct",9} {"Ratio",7}  Type");
        Console.WriteLine($"  {new string('-', 75)}");

        foreach (var conn in byConn.OrderBy(g => g.Min(c => c.Timestamp)))
        {
            int    total    = conn.Count();
            int    distinct = conn.Select(c => c.SerialNumber).Distinct().Count();
            double ratio    = (double)distinct / total;
            bool   isTest   = ratio < 0.5 || distinct <= 1;
            string type     = isTest ? "TEST  (skipped)" : "PRODUCTION";

            Console.WriteLine($"  {conn.Key,-45} {total,6} {distinct,9} {ratio,7:P1}  {type}");

            if (!isTest)
            {
                productionStreamKeys.Add(conn.Key);
                var times = conn.Select(c => c.Timestamp).ToList();
                if (times.Min() < prodStart) prodStart = times.Min();
                if (times.Max() > prodEnd)   prodEnd   = times.Max();
            }
        }

        if (productionStreamKeys.Count == 0)
        {
            Console.WriteLine("  WARNING: no production connection found; analysing all data.");
            prodStart = DateTime.MinValue;
            prodEnd   = DateTime.MaxValue;
            foreach (var g in byConn) productionStreamKeys.Add(g.Key);
        }

        // Extend window slightly to capture trailing notifications
        prodEnd = prodEnd.AddSeconds(60);
        Console.WriteLine();
        Console.WriteLine($"  Production time window: {prodStart:yyyy-MM-dd HH:mm:ss.fff} – {prodEnd:yyyy-MM-dd HH:mm:ss.fff}");

        return new TimeRange(prodStart, prodEnd);
    }

    // ── Buffer flow ──────────────────────────────────────────────────────────

    private static void AnalyzeBufferFlow(List<Command55> cmds,
                                          List<Notification06> prints)
    {
        var inUse    = new HashSet<int>();
        int maxInUse = 0, overlaps = 0;

        var events = cmds
            .Select(c => (c.Timestamp, 'C', (int)c.BufferNumber))
            .Concat(prints.Select(n => (n.Timestamp, 'N', (int)n.BufferNumber)))
            .OrderBy(e => e.Timestamp);

        foreach (var (ts, kind, buf) in events)
        {
            if (kind == 'C')
            {
                if (inUse.Contains(buf))
                {
                    Console.WriteLine($"  [!] OVERLAP  buf={buf,-2} at {ts:HH:mm:ss.fff}  " +
                                      $"(0x55 before previous print completed)");
                    overlaps++;
                }
                inUse.Add(buf);
            }
            else
            {
                inUse.Remove(buf);
            }
            maxInUse = Math.Max(maxInUse, inUse.Count);
        }

        int maxQ  = prints.Count > 0 ? prints.Max(n => n.QueueCount) : 0;
        double avgQ = prints.Count > 0 ? prints.Average(n => (double)n.QueueCount) : 0;

        Console.WriteLine($"  Max buffers in-use simultaneously : {maxInUse} / 32");
        Console.WriteLine($"  Buffer overlap events             : {overlaps}");
        Console.WriteLine($"  Queue depth (0x06 field)          : max={maxQ}  avg={avgQ:F1}");
    }

    // ── Serial numbers ───────────────────────────────────────────────────────

    private static void AnalyzeSerialNumbers(List<Command55> cmds)
    {
        if (cmds.Count == 0) { Console.WriteLine("  (none)"); return; }

        var dups = cmds
            .GroupBy(c => c.SerialNumber)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToList();

        int distinct = cmds.Select(c => c.SerialNumber).Distinct().Count();
        Console.WriteLine($"  Unique serials : {distinct}  (of {cmds.Count} commands)");

        if (dups.Count == 0)
        {
            Console.WriteLine("  No duplicates. ✓");
        }
        else
        {
            Console.WriteLine($"  [!] {dups.Count} serial(s) sent more than once:");
            foreach (var g in dups.Take(20))
            {
                Console.WriteLine($"      \"{g.Key}\" × {g.Count()}  " +
                                  $"first={g.Min(c => c.Timestamp):HH:mm:ss.fff}  " +
                                  $"last={g.Max(c => c.Timestamp):HH:mm:ss.fff}");
            }
            if (dups.Count > 20) Console.WriteLine($"      ...and {dups.Count - 20} more");
        }
    }

    // ── Enhanced drop-delta analysis ─────────────────────────────────────────

    private static void AnalyzeDropDeltas(List<Notification06> prints)
    {
        if (prints.Count < 2) { Console.WriteLine("  Need at least 2 prints."); return; }

        // Detect active pens (ever fired a non-zero delta)
        var activePens = new bool[4];
        Notification06? probe = null;
        foreach (var n in prints)
        {
            if (probe != null)
                for (int p = 0; p < 4; p++)
                    if ((long)n.DropsPerPen[p] - probe.DropsPerPen[p] != 0) activePens[p] = true;
            probe = n;
        }
        Console.WriteLine("  Active pens  : " +
            string.Join(", ", Enumerable.Range(0, 4).Where(p =>  activePens[p]).Select(p => $"P{p+1}")));
        Console.WriteLine("  Inactive pens: " +
            string.Join(", ", Enumerable.Range(0, 4).Where(p => !activePens[p]).Select(p => $"P{p+1}")));
        Console.WriteLine();

        Notification06? prevGlobal = null;
        long[]?         prevGlobDlt = null;                          // per-print delta of last print
        var             lastDltByBuf = new Dictionary<int, long[]>(); // per-print delta per buffer

        var adjDups     = new List<(int idx, byte buf, string detail)>();
        var sameBufDups = new List<(int idx, byte buf, string detail)>();
        var penEvents   = new List<(int idx, byte buf, int pen, string kind)>();

        Console.WriteLine($"  {"#",-6} {"Buf",-4} {"ΔP1",-10} {"ΔP2",-10} {"ΔP3",-10} {"ΔP4",-10} Flags");
        Console.WriteLine($"  {new string('-', 75)}");

        foreach (var n in prints)
        {
            if (prevGlobal == null) { prevGlobal = n; continue; }

            // Per-print drops delta vs global previous notification
            var delta = new long[4];
            for (int p = 0; p < 4; p++)
                delta[p] = (long)n.DropsPerPen[p] - prevGlobal.DropsPerPen[p];

            var flags = new List<string>();

            // ── Check 1: this print's per-pen delta == previous print's delta ──
            if (prevGlobDlt != null)
            {
                bool allActiveSame = !activePens.Any(a => a) ||
                    Enumerable.Range(0, 4).Where(p => activePens[p]).All(p => delta[p] == prevGlobDlt[p]);

                if (allActiveSame)
                {
                    flags.Add("ADJ-DUP");
                    adjDups.Add((n.SeqIndex, n.BufferNumber,
                        $"active-pen deltas identical to prev print #{prevGlobal.SeqIndex} buf={prevGlobal.BufferNumber}"));
                }
                else
                {
                    for (int p = 0; p < 4; p++)
                        if (activePens[p] && delta[p] == prevGlobDlt[p])
                        {
                            flags.Add($"P{p+1}-ADJ-DUP");
                            penEvents.Add((n.SeqIndex, n.BufferNumber, p + 1, "adj"));
                        }
                }
            }

            // ── Check 2: this print's per-pen delta == last time this buffer ran ──
            if (lastDltByBuf.TryGetValue(n.BufferNumber, out var prevBufDlt))
            {
                bool allActiveSame = !activePens.Any(a => a) ||
                    Enumerable.Range(0, 4).Where(p => activePens[p]).All(p => delta[p] == prevBufDlt[p]);

                if (allActiveSame)
                {
                    flags.Add("SBUF-DUP");
                    sameBufDups.Add((n.SeqIndex, n.BufferNumber,
                        $"active-pen deltas same as previous use of buf={n.BufferNumber}"));
                }
                else
                {
                    for (int p = 0; p < 4; p++)
                        if (activePens[p] && delta[p] == prevBufDlt[p])
                        {
                            flags.Add($"P{p+1}-SBUF-DUP");
                            penEvents.Add((n.SeqIndex, n.BufferNumber, p + 1, "same-buf"));
                        }
                }
            }

            bool showRow = flags.Count > 0 ||
                           n.SeqIndex <= prints[Math.Min(4, prints.Count - 1)].SeqIndex ||
                           n.SeqIndex >= prints[Math.Max(0, prints.Count - 5)].SeqIndex;

            if (showRow)
                Console.WriteLine(
                    $"  {n.SeqIndex,-6} {n.BufferNumber,-4} " +
                    $"{delta[0],-10} {delta[1],-10} {delta[2],-10} {delta[3],-10} " +
                    $"{(flags.Count > 0 ? "⚠ " + string.Join(" ", flags) : "")}");

            prevGlobal   = n;
            prevGlobDlt  = delta;
            lastDltByBuf[n.BufferNumber] = delta;
        }

        Console.WriteLine();
        Console.WriteLine("  ── Delta Anomaly Summary ─────────────────────────────────────────");
        Console.WriteLine($"  Full-signature adjacent duplicates  (ADJ-DUP)   : {adjDups.Count}");
        Console.WriteLine($"  Full-signature same-buffer duplicates (SBUF-DUP) : {sameBufDups.Count}");
        Console.WriteLine($"  Single-pen adjacent duplicates  (Px-ADJ-DUP)    : {penEvents.Count(e => e.kind == "adj")}");
        Console.WriteLine($"  Single-pen same-buffer duplicates (Px-SBUF-DUP) : {penEvents.Count(e => e.kind == "same-buf")}");

        foreach (var (label, list) in new[]
        {
            ("⚠ Full-signature adjacent duplicates:", adjDups.Select(x => $"#{x.idx,-6} buf={x.buf,-2}  {x.detail}").ToList()),
            ("⚠ Full-signature same-buffer duplicates:", sameBufDups.Select(x => $"#{x.idx,-6} buf={x.buf,-2}  {x.detail}").ToList()),
        })
        {
            if (list.Count > 0) { Console.WriteLine(); Console.WriteLine($"  {label}"); foreach (var l in list) Console.WriteLine($"    {l}"); }
        }

        if (penEvents.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ⚠ Single-pen duplicate events:");
            foreach (var (idx, buf, pen, kind) in penEvents)
                Console.WriteLine($"    #{idx,-6} buf={buf,-2}  pen=P{pen}  check={kind}");
        }

        if (adjDups.Count == 0 && sameBufDups.Count == 0 && penEvents.Count == 0)
            Console.WriteLine("  All active-pen deltas are unique. ✓");
    }

    // ── Anomaly summary ───────────────────────────────────────────────────────

    // ── Serial forecast for full-signature duplicates ────────────────────────

    /// <summary>
    /// For each full-signature duplicate (both active pens show identical per-print delta),
    /// determines which serial number was physically printed instead of the intended one.
    ///
    /// Correlation rule: for a given 0x06 notification freeing buffer B at time T,
    /// the serial that was loaded into that buffer is the serial from the LAST 0x55 command
    /// for buffer B whose timestamp ≤ T.
    /// </summary>
    // ── Full-signature duplicate scan ─────────────────────────────────────────

    /// <summary>
    /// Standalone duplicate detector — no external input needed.
    ///
    /// For each real print, computes the full ink-drop signature (P1Δ, P2Δ, …
    /// for every active pen).  Looks back through ALL previous prints for any
    /// match on the complete signature (same Δ on every active pen simultaneously).
    ///
    /// A match means both pens fired the exact same number of drops → the printer
    /// physically reproduced an old buffer image instead of the new one.
    ///
    /// Three match types:
    ///   SBUF   – signature already seen on the same buffer number (clearest: buffer not reloaded)
    ///   ADJ    – signature identical to the immediately preceding print (head stuck at trigger)
    ///   GLOBAL – signature seen on a different buffer (head used wrong buffer slot entirely)
    ///
    /// Reports: REPLAYED serial (printed twice) and DISPLACED serial (never printed).
    /// </summary>
    private static void ScanForDuplicates(List<Command55> cmds,
                                           List<Notification06> prints)
    {
        const long NoData = long.MinValue;
        if (prints.Count < 2) { Console.WriteLine("  (insufficient data)"); return; }

        // --- serial lookup ---
        var cmdsByBuf = cmds
            .GroupBy(c => (int)c.BufferNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Timestamp).ToList());

        string GetSerial(Notification06 n)
        {
            if (!cmdsByBuf.TryGetValue(n.BufferNumber, out var list)) return "(none)";
            var m = list.LastOrDefault(e => e.Timestamp <= n.Timestamp);
            return m == default ? "(none)" : m.SerialNumber.Replace("\r","").Replace("\n","").Trim();
        }

        int N = prints.Count;
        var serials = new string[N];
        var deltas  = new long[4][];
        for (int p = 0; p < 4; p++) deltas[p] = new long[N];

        for (int i = 0; i < N; i++)
        {
            serials[i] = GetSerial(prints[i]);
            for (int p = 0; p < 4; p++)
                deltas[p][i] = i == 0 ? NoData
                    : (long)prints[i].DropsPerPen[p] - prints[i-1].DropsPerPen[p];
        }

        // detect active pens
        var active = new bool[4];
        for (int i = 1; i < N; i++)
            for (int p = 0; p < 4; p++)
                if (deltas[p][i] != NoData && deltas[p][i] != 0) active[p] = true;

        int[] activePens = Enumerable.Range(0, 4).Where(p => active[p]).ToArray();
        Console.WriteLine($"  Active pens: {string.Join(", ", activePens.Select(p => $"P{p+1}"))}");
        Console.WriteLine();

        // --- index: (p0Δ, p1Δ, ...) → list of previous print positions ---
        // Key: concatenated active-pen deltas as a long[]
        // Use a Dictionary keyed by a value tuple of the active pen deltas
        // Since C# supports up to 8-element value tuple equality, build a string key
        string SigKey(int i) =>
            string.Join("|", activePens.Select(p => deltas[p][i].ToString()));

        // Pre-compute total occurrences of each signature across the full stream
        // to suppress common signatures in GLOBAL scan (collision filter).
        // A pair seen more than MAX_GLOBAL_FREQ times is too common to be diagnostic.
        const int MaxGlobalFreq = 4;
        var sigFreq = new Dictionary<string, int>();
        for (int i = 1; i < N; i++)
        {
            if (activePens.Any(p => deltas[p][i] == NoData)) continue;
            string s = SigKey(i);
            sigFreq[s] = sigFreq.TryGetValue(s, out int f) ? f + 1 : 1;
        }

        var sigHistory = new Dictionary<string, List<int>>();

        // per-buffer: last signature
        var lastBufSig = new Dictionary<int, (string sig, int idx)>();

        var events = new List<(string kind, int i, int j, string replayed, string displaced)>();

        for (int i = 1; i < N; i++)
        {
            // skip if any active pen has NoData (first print ever)
            if (activePens.Any(p => deltas[p][i] == NoData)) goto UpdateHistory;

            // skip all-zero signature (head fired nothing — not a real image)
            if (activePens.All(p => deltas[p][i] == 0)) goto UpdateHistory;

            string sig = SigKey(i);
            int buf    = prints[i].BufferNumber;

            // ── SBUF: same buffer, same signature ────────────────────────────
            if (lastBufSig.TryGetValue(buf, out var prev) && prev.sig == sig)
            {
                events.Add(("SBUF  ", i, prev.idx, serials[prev.idx], serials[i]));
                goto UpdateHistory;   // don't also report ADJ/GLOBAL for same event
            }

            // ── ADJ: previous print (any buffer), same signature ─────────────
            if (i >= 2 && SigKey(i-1) == sig && !activePens.Any(p => deltas[p][i-1] == NoData))
            {
                events.Add(("ADJ   ", i, i-1, serials[i-1], serials[i]));
                goto UpdateHistory;
            }

            // ── GLOBAL: only in InvestigateKnownDuplicates (too noisy standalone)
            // Omitted here — the (P1Δ,P2Δ) pair collision rate across 11K+ prints
            // produces too many false positives without an external anchor serial.

            UpdateHistory:
            {
                int buf2 = prints[i].BufferNumber;
                if (activePens.Any(p => deltas[p][i] != NoData))
                {
                    string sig2 = SigKey(i);
                    if (!sigHistory.TryGetValue(sig2, out var lst2)) sigHistory[sig2] = lst2 = new();
                    lst2.Add(i);
                    lastBufSig[buf2] = (sig2, i);
                }
            }
        }

        if (events.Count == 0)
        {
            Console.WriteLine("  ✓ No full-signature duplicate events detected.");
            return;
        }

        Console.WriteLine($"  {"Type",-8} {"#Dup",-8} {"Buf",-4} {"Time",-14} " +
                          $"{"REPLAYED (printed twice)",-26} {"DISPLACED (never printed)",-26} Ref print");
        Console.WriteLine($"  {new string('-', 110)}");

        foreach (var (kind, i, j, replayed, displaced) in events)
        {
            var ni = prints[i];
            var nj = prints[j];
            string sigStr = string.Join(" ", activePens.Select(p => $"P{p+1}Δ={deltas[p][i]}"));
            Console.WriteLine($"  {kind} #{ni.SeqIndex,-6} {ni.BufferNumber,-4} " +
                              $"{ni.Timestamp:HH:mm:ss}  " +
                              $"{replayed,-26} {displaced,-26} " +
                              $"ref=#{nj.SeqIndex} buf={nj.BufferNumber}");
        }

        Console.WriteLine();

        // de-duplicate across event list
        var replayedSet  = events.Select(e => e.replayed.ToUpperInvariant()).ToHashSet();
        var displacedSet = events.Select(e => e.displaced.ToUpperInvariant()).ToHashSet();
        var replayedList  = events.Select(e => e.replayed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var displacedList = events.Select(e => e.displaced).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        int sbuf   = events.Count(e => e.kind.StartsWith("SBUF"));
        int adj    = events.Count(e => e.kind.StartsWith("ADJ"));
        int global = events.Count(e => e.kind.StartsWith("GLOBAL"));

        Console.WriteLine($"  Events : {events.Count} total  " +
                          $"(SBUF={sbuf}, ADJ={adj}, GLOBAL={global})");
        Console.WriteLine($"  ❌ Serials printed TWICE  : {replayedList.Count}");
        foreach (var s in replayedList) Console.WriteLine($"      {s}");
        Console.WriteLine($"  ❓ Serials NEVER printed  : {displacedList.Count}");
        foreach (var s in displacedList) Console.WriteLine($"      {s}  ← verify absent from camera log");
    }

    // ── Anomaly summary ───────────────────────────────────────────────────────

    private static void PrintAnomalySummary(List<Command55> cmds,
                                            List<Notification06> prints)
    {
        var issues = new List<string>();

        int dupSerials = cmds.GroupBy(c => c.SerialNumber).Count(g => g.Count() > 1);
        if (dupSerials > 0) issues.Add($"Duplicate serial numbers in commands: {dupSerials}");

        int errors = prints.Count(n => n.OverallStatus != 0 || n.PenStatus.Any(p => p != 0));
        if (errors > 0) issues.Add($"Print status errors (pen/sync ≠ 0): {errors}");

        int syncLost = prints.Count(n => n.OverallStatus == 2);
        if (syncLost > 0) issues.Add($"Sync-lost events: {syncLost}");

        // full-signature duplicate counts (mirrors ScanForDuplicates logic)
        var active = new bool[4];
        Notification06? probe = null;
        foreach (var n in prints)
        {
            if (probe != null)
                for (int p = 0; p < 4; p++)
                    if ((long)n.DropsPerPen[p] - probe.DropsPerPen[p] != 0) active[p] = true;
            probe = n;
        }
        int[] activePens = Enumerable.Range(0, 4).Where(p => active[p]).ToArray();

        int N = prints.Count;
        var deltas = new long[4][];
        for (int p = 0; p < 4; p++) deltas[p] = new long[N];
        for (int i = 1; i < N; i++)
            for (int p = 0; p < 4; p++)
                deltas[p][i] = (long)prints[i].DropsPerPen[p] - prints[i-1].DropsPerPen[p];

        string SigKey(int i) =>
            string.Join("|", activePens.Select(p => deltas[p][i].ToString()));

        var sigHistory  = new Dictionary<string, List<int>>();
        var lastBufSig  = new Dictionary<int, (string sig, int idx)>();
        int sbuf = 0, adj = 0, global = 0;

        for (int i = 1; i < N; i++)
        {
            if (activePens.Any(p => deltas[p][i] == 0 && p == 0)) { /* keep going */ }
            string sig = SigKey(i);
            int buf    = prints[i].BufferNumber;

            if (lastBufSig.TryGetValue(buf, out var prevB) && prevB.sig == sig) { sbuf++; goto upd; }
            if (i >= 2 && SigKey(i-1) == sig) { adj++; goto upd; }
            if (sigHistory.TryGetValue(sig, out _)) global++;

            upd:
            if (!sigHistory.TryGetValue(sig, out var lst)) sigHistory[sig] = lst = new();
            lst.Add(i);
            lastBufSig[buf] = (sig, i);
        }

        if (sbuf   > 0) issues.Add($"Full-signature same-buffer duplicates (SBUF): {sbuf}");
        if (adj    > 0) issues.Add($"Full-signature adjacent duplicates (ADJ):      {adj}");
        // GLOBAL duplicates surfaced only via InvestigateKnownDuplicates (external anchor needed)

        if (issues.Count == 0)
            Console.WriteLine("  No anomalies. ✓");
        else
        {
            Console.WriteLine($"  {issues.Count} anomaly type(s):\n");
            foreach (var i in issues) Console.WriteLine($"  ❌ {i}");
        }
    }

    // ── Blank OCR analysis ─────────────────────────────────────────────────────

    /// <summary>
    /// For each serial where the camera read the datamatrix successfully but got
    /// a blank/unreadable OCR (P2 text field), find the print in the pcap and
    /// examine:
    ///   • P2Δ — is it zero (P2 didn't fire)?  Does it match the previous print
    ///     of the same buffer (P2 replayed old content)?  Does it match the
    ///     adjacent print (P2 used wrong buffer)?
    ///   • P1Δ — was P1 also anomalous or was it normal?
    ///   • Context window for the surrounding prints.
    /// </summary>
    private static void AnalyzeBlankOcr(List<Command55> cmds,
                                         List<Notification06> prints,
                                         string[] blankOcrSerials)
    {
        const long NoData = long.MinValue;

        // --- build serial lookup from command stream ---
        var cmdsByBuf = cmds
            .GroupBy(c => (int)c.BufferNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Timestamp).ToList());

        string GetSerial(Notification06 n)
        {
            if (!cmdsByBuf.TryGetValue(n.BufferNumber, out var list)) return "(none)";
            var match = list.LastOrDefault(e => e.Timestamp <= n.Timestamp);
            return match == default ? "(none)"
                : match.SerialNumber.Replace("\r","").Replace("\n","").Trim();
        }

        int N = prints.Count;
        var serials  = new string[N];
        var deltas   = new long[4][];
        for (int p = 0; p < 4; p++) deltas[p] = new long[N];

        for (int i = 0; i < N; i++)
        {
            serials[i] = GetSerial(prints[i]);
            for (int p = 0; p < 4; p++)
                deltas[p][i] = i == 0 ? NoData
                    : (long)prints[i].DropsPerPen[p] - prints[i-1].DropsPerPen[p];
        }

        // per-buffer: last seen P1Δ and P2Δ
        var lastBufP1 = new Dictionary<int, (long d, int idx)>();
        var lastBufP2 = new Dictionary<int, (long d, int idx)>();
        var prevBufP1 = new (long d, int idx)[N];
        var prevBufP2 = new (long d, int idx)[N];
        for (int i = 0; i < N; i++)
        {
            prevBufP1[i] = (NoData, -1);
            prevBufP2[i] = (NoData, -1);
        }
        for (int i = 1; i < N; i++)
        {
            int buf = prints[i].BufferNumber;
            if (lastBufP1.TryGetValue(buf, out var lp1)) prevBufP1[i] = lp1;
            if (lastBufP2.TryGetValue(buf, out var lp2)) prevBufP2[i] = lp2;
            if (deltas[0][i] != NoData) lastBufP1[buf] = (deltas[0][i], i);
            if (deltas[1][i] != NoData) lastBufP2[buf] = (deltas[1][i], i);
        }

        // serial → print index map
        var serialIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < N; i++)
            if (!serialIdx.ContainsKey(serials[i])) serialIdx[serials[i]] = i;

        Console.WriteLine($"  {"Serial",-24} {"#Print",-8} {"Buf",-4} {"P2Δ",-10} " +
                          $"{"P2 diagnosis",-34} {"P1Δ",-10} {"P1 diagnosis"}");
        Console.WriteLine($"  {new string('-', 110)}");

        foreach (var raw in blankOcrSerials)
        {
            string ks = raw.Replace("\r","").Replace("\n","").Trim();

            if (!serialIdx.TryGetValue(ks, out int i))
            {
                Console.WriteLine($"  {ks,-24} NOT FOUND in pcap");
                continue;
            }

            var n    = prints[i];
            long p2d = deltas[1][i];
            long p1d = deltas[0][i];

            // ── P2 diagnosis ──────────────────────────────────────────────────
            string p2diag;
            if (p2d == NoData)
                p2diag = "first print (no prev)";
            else if (p2d == 0)
                p2diag = "⚠ P2Δ=0 — P2 DID NOT FIRE";
            else
            {
                bool adjMatch  = i > 1 && deltas[1][i-1] != NoData && p2d == deltas[1][i-1];
                bool sbufMatch = prevBufP2[i].d != NoData && p2d == prevBufP2[i].d;

                if (adjMatch && sbufMatch)
                    p2diag = $"⚠ P2 ADJ+SBUF replay (buf prev print #{prints[prevBufP2[i].idx].SeqIndex})";
                else if (sbufMatch)
                    p2diag = $"⚠ P2 SBUF replay (same as buf #{prints[prevBufP2[i].idx].SeqIndex})";
                else if (adjMatch)
                    p2diag = $"⚠ P2 ADJ replay (same as print #{prints[i-1].SeqIndex})";
                else
                    p2diag = "OK (unique P2Δ)";
            }

            // ── P1 diagnosis ──────────────────────────────────────────────────
            string p1diag;
            if (p1d == NoData)
                p1diag = "first print";
            else if (p1d == 0)
                p1diag = "⚠ P1Δ=0 — P1 DID NOT FIRE";
            else
            {
                bool adjMatch  = i > 1 && deltas[0][i-1] != NoData && p1d == deltas[0][i-1];
                bool sbufMatch = prevBufP1[i].d != NoData && p1d == prevBufP1[i].d;
                if (adjMatch && sbufMatch) p1diag = "⚠ P1 ADJ+SBUF replay";
                else if (sbufMatch)        p1diag = $"⚠ P1 SBUF replay (buf #{prints[prevBufP1[i].idx].SeqIndex})";
                else if (adjMatch)         p1diag = $"⚠ P1 ADJ replay";
                else                       p1diag = "OK";
            }

            string p2dStr = p2d == NoData ? "n/a" : p2d.ToString();
            string p1dStr = p1d == NoData ? "n/a" : p1d.ToString();
            Console.WriteLine($"  {ks,-24} #{n.SeqIndex,-6} {n.BufferNumber,-4} {p2dStr,-10} {p2diag,-34} {p1dStr,-10} {p1diag}");
        }

        // context windows for anomalous entries
        Console.WriteLine();
        Console.WriteLine("  ── Context windows for anomalous prints ──");
        foreach (var raw in blankOcrSerials)
        {
            string ks = raw.Replace("\r","").Replace("\n","").Trim();
            if (!serialIdx.TryGetValue(ks, out int i)) continue;

            long p2d = deltas[1][i];
            bool anomalous = p2d == NoData || p2d == 0 ||
                             (i > 1 && deltas[1][i-1] != NoData && p2d == deltas[1][i-1]) ||
                             (prevBufP2[i].d != NoData && p2d == prevBufP2[i].d);
            if (!anomalous) continue;

            var n = prints[i];
            int lo = Math.Max(1, i - 4), hi = Math.Min(N - 1, i + 4);
            Console.WriteLine();
            Console.WriteLine($"  {ks}  print #{n.SeqIndex}  buf={n.BufferNumber}  {n.Timestamp:HH:mm:ss.fff}");
            Console.WriteLine($"    {"#Seq",-8} {"Buf",-4} {"P1Δ",-10} {"P2Δ",-10} Serial");
            for (int ci = lo; ci <= hi; ci++)
            {
                var cn  = prints[ci];
                string mark = ci == i ? "◄" : " ";
                string p1s  = deltas[0][ci] == NoData ? "n/a" : deltas[0][ci].ToString();
                string p2s  = deltas[1][ci] == NoData ? "n/a" : deltas[1][ci].ToString();
                Console.WriteLine($"    {mark} #{cn.SeqIndex,-6} {cn.BufferNumber,-4} {p1s,-10} {p2s,-10} {serials[ci]}");
            }
        }
    }

    // ── Known-duplicate investigation ─────────────────────────────────────────

    /// <summary>
    /// Comprehensive investigation of known-duplicate serials.
    ///
    /// For each known-duplicate serial:
    ///   1. Find its print position in pcap (or flag as NOT FOUND in 0x55 commands).
    ///   2. For all pens (P1/P2), find EVERY future print of the SAME buffer number
    ///      where the per-print drop delta equals the original print's delta
    ///      → that future print's buffer was replayed → the serial sent to that
    ///        buffer just before the notification is the DISPLACED serial.
    ///   3. Also show a ±5 context window of prints around the original to
    ///      reveal near-duplicate adjacencies at any offset.
    ///   4. Collect and de-duplicate all displaced serials for final verdict.
    /// </summary>
    private static void InvestigateKnownDuplicates(List<Command55> cmds,
                                                    List<Notification06> prints,
                                                    string[] knownDuplicates)
    {
        const long NoData = long.MinValue;

        var knownSet = knownDuplicates
            .Select(s => s.Replace("\r","").Replace("\n","").Trim().ToUpperInvariant())
            .ToHashSet();

        // --- build serial lookup from command stream ---
        var cmdsByBuf = cmds
            .GroupBy(c => (int)c.BufferNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Timestamp).ToList());

        string GetSerial(Notification06 n)
        {
            if (!cmdsByBuf.TryGetValue(n.BufferNumber, out var list)) return "(none)";
            var match = list.LastOrDefault(e => e.Timestamp <= n.Timestamp);
            return match == default ? "(none)"
                : match.SerialNumber.Replace("\r","").Replace("\n","").Trim();
        }

        // --- build per-print delta arrays for all 4 pens ---
        int N = prints.Count;
        var serials  = new string[N];
        var deltas   = new long[4][];
        for (int p = 0; p < 4; p++) deltas[p] = new long[N];

        for (int i = 0; i < N; i++)
        {
            serials[i] = GetSerial(prints[i]);
            for (int p = 0; p < 4; p++)
                deltas[p][i] = i == 0 ? NoData
                    : (long)prints[i].DropsPerPen[p] - prints[i - 1].DropsPerPen[p];
        }

        // detect active pens
        var activePens = new bool[4];
        for (int i = 1; i < N; i++)
            for (int p = 0; p < 4; p++)
                if (deltas[p][i] != NoData && deltas[p][i] != 0) activePens[p] = true;

        // build index: delta value → list of print indices (per pen)
        var deltaIndex = new Dictionary<long, List<int>>[4];
        for (int p = 0; p < 4; p++)
        {
            deltaIndex[p] = new Dictionary<long, List<int>>();
            for (int i = 1; i < N; i++)
            {
                long d = deltas[p][i];
                if (d == NoData) continue;
                if (!deltaIndex[p].TryGetValue(d, out var lst)) deltaIndex[p][d] = lst = new();
                lst.Add(i);
            }
        }

        // build index: buf → list of print indices
        var bufPrints = new Dictionary<int, List<int>>();
        for (int i = 0; i < N; i++)
        {
            int b = prints[i].BufferNumber;
            if (!bufPrints.TryGetValue(b, out var lst)) bufPrints[b] = lst = new();
            lst.Add(i);
        }

        // serial → print indices
        var serialIdx = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < N; i++)
        {
            string s = serials[i];
            if (!serialIdx.TryGetValue(s, out var lst)) serialIdx[s] = lst = new();
            lst.Add(i);
        }

        // --- per-known-duplicate analysis ---
        var allDisplaced = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // displaced serial → list of "how discovered"

        foreach (var raw in knownDuplicates)
        {
            string ks = raw.Replace("\r","").Replace("\n","").Trim();
            string ksUp = ks.ToUpperInvariant();

            Console.WriteLine();
            Console.WriteLine($"  ════ {ks} ════");

            // locate in print stream
            if (!serialIdx.TryGetValue(ks, out var origIdxList) || origIdxList.Count == 0)
            {
                Console.WriteLine("    ⚠ NOT FOUND as a 0x55 command in pcap.");
                Console.WriteLine("      → The printer may have replayed an old buffer for this serial,");
                Console.WriteLine("        meaning this serial appears physically but no new data was loaded.");
                Console.WriteLine("        Check which buffer printed JUST BEFORE the expected position.");
                continue;
            }

            foreach (int origI in origIdxList)
            {
                var origN = prints[origI];
                int buf   = origN.BufferNumber;
                Console.WriteLine($"    Original: print #{origN.SeqIndex}  buf={buf}  {origN.Timestamp:HH:mm:ss.fff}");

                // context window
                int lo = Math.Max(1, origI - 5), hi = Math.Min(N - 1, origI + 5);
                Console.WriteLine($"    Context window prints [{lo}..{hi}]:");
                Console.WriteLine($"      {"#Seq",-8} {"Buf",-4} {"P1Δ",-10} {"P2Δ",-10} Serial");
                for (int ci = lo; ci <= hi; ci++)
                {
                    var cn = prints[ci];
                    string mark = ci == origI ? "◄" : " ";
                    string p1d  = deltas[0][ci] == NoData ? "n/a" : deltas[0][ci].ToString();
                    string p2d  = deltas[1][ci] == NoData ? "n/a" : deltas[1][ci].ToString();
                    Console.WriteLine($"      {mark} #{cn.SeqIndex,-6} {cn.BufferNumber,-4} {p1d,-10} {p2d,-10} {serials[ci]}");
                }

                // ── P2 (text pen) global sweep ──────────────────────────────────
                // P2 prints the human-readable serial text — drop collisions are rare
                // → use P2Δ as the primary duplicate discriminator globally (any buffer).
                // P1 prints the datamatrix — collisions are frequent → use only to
                // UPGRADE confidence when it also matches, not as primary signal.

                long p2orig = deltas[1][origI];
                long p1orig = deltas[0][origI];

                Console.WriteLine($"    P2Δ={p2orig}  P1Δ={p1orig}");

                if (p2orig == NoData || p2orig == 0)
                {
                    Console.WriteLine("    P2Δ is zero or unavailable — P2 may print static content for this serial.");
                    Console.WriteLine("    Falling back to same-buffer P2Δ search with non-zero check skipped:");
                }

                // Global future P2Δ matches (any buffer)
                bool anyP2 = false;
                if (deltaIndex[1].TryGetValue(p2orig, out var p2hits))
                {
                    var futureP2 = p2hits.Where(j => j > origI).OrderBy(j => j).ToList();
                    if (futureP2.Count > 0)
                    {
                        Console.WriteLine($"    Global P2Δ={p2orig} future matches: {futureP2.Count}");
                        Console.WriteLine($"      {"Conf",-6} {"#Seq",-8} {"Buf",-4} {"SameBuf",-8} {"P1match",-8} {"Displaced serial",-24} Time");
                        Console.WriteLine($"      {new string('-', 80)}");

                        foreach (int fi in futureP2)
                        {
                            anyP2 = true;
                            var fn       = prints[fi];
                            bool sameBuf = fn.BufferNumber == buf;
                            bool p1match = p1orig != NoData && p1orig != 0
                                           && deltas[0][fi] == p1orig;
                            string conf  = p1match ? "HIGH" : (sameBuf ? "MED" : "LOW");
                            string disp  = serials[fi];

                            Console.WriteLine($"      {conf,-6} #{fn.SeqIndex,-6} {fn.BufferNumber,-4} " +
                                              $"{(sameBuf ? "yes" : ""),-8} " +
                                              $"{(p1match ? "yes" : ""),-8} " +
                                              $"{disp,-24} {fn.Timestamp:HH:mm:ss.fff}");

                            // Only record as displaced if confidence ≥ MED (same-buf or P1-also-matches)
                            if (sameBuf || p1match)
                            {
                                string key = disp.ToUpperInvariant();
                                if (!allDisplaced.TryGetValue(key, out var how))
                                    allDisplaced[key] = how = new();
                                string tag = p1match ? $"{ks}→buf{fn.BufferNumber}@#{fn.SeqIndex}[HIGH]"
                                                     : $"{ks}→buf{fn.BufferNumber}@#{fn.SeqIndex}[MED]";
                                how.Add(tag);
                            }
                        }
                    }
                }

                if (!anyP2)
                    Console.WriteLine($"    No global P2Δ={p2orig} future matches found.");
            }
        }

        // --- summary ---
        Console.WriteLine();
        Console.WriteLine($"  ── DISPLACED SERIALS SUMMARY (MED/HIGH confidence only) ──");
        Console.WriteLine($"    Conf=HIGH: P2Δ matched (text replay) AND P1Δ matched (datamatrix replay) → both pens replayed, label fully wrong");
        Console.WriteLine($"    Conf=MED : P2Δ matched on same buffer → text field replayed (datamatrix was correct, camera still read new serial)");
        Console.WriteLine();
        if (allDisplaced.Count == 0)
        {
            Console.WriteLine("  None found at MED/HIGH confidence.");
            Console.WriteLine("  LOW-confidence global P2Δ hits shown above (different buffer, P1 not matching).");
        }
        else
        {
            Console.WriteLine($"    {"Conf",-6} {"Displaced serial",-24}  Evidence");
            Console.WriteLine($"    {new string('-', 90)}");
            foreach (var kv in allDisplaced.OrderByDescending(x => x.Value.Any(v => v.Contains("[HIGH]"))).ThenBy(x => x.Key))
            {
                string repr  = serials.FirstOrDefault(s => string.Equals(s, kv.Key, StringComparison.OrdinalIgnoreCase)) ?? kv.Key;
                string conf  = kv.Value.Any(v => v.Contains("[HIGH]")) ? "HIGH" : "MED";
                Console.WriteLine($"    {conf,-6} {repr,-24}  {string.Join("; ", kv.Value)}");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Normalise to "clientIP:clientPort" (the side that is NOT port 10000/10001)
    private static string NormKey(string streamKey)
    {
        // streamKey format:  "srcIP:srcPort->dstIP:dstPort"
        var parts = streamKey.Split("->", 2);
        if (parts.Length != 2) return streamKey;
        // The printer is on a well-known port; the client port is the variable one
        var src = parts[0]; // e.g. "192.168.1.5:54321"
        var dst = parts[1]; // e.g. "192.168.1.2:10000"
        return dst.EndsWith(":10000") || dst.EndsWith(":10001")
            ? src   // client is source
            : dst;  // client is destination
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"┌─ {title} {new string('─', Math.Max(0, 65 - title.Length))}");
    }

    // ── Inner type ────────────────────────────────────────────────────────────
    private readonly record struct TimeRange(DateTime Start, DateTime End)
    {
        public bool Contains(DateTime t) => t >= Start && t <= End;
    }
}

