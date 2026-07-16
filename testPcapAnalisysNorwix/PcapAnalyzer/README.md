# PcapAnalyzer — Inkjet Printer Duplicate Print Detector

Analyzes a Wireshark/tcpdump network capture of the proprietary binary protocol
used by the Norvik/Nortek inkjet printing system to detect **duplicate prints**
(same physical label printed twice) and **displaced serials** (labels never
physically produced).

---

## Quick Start

```
dotnet run -c Release -- "<path-to-capture.pcap>"
```

The tool auto-selects the production TCP connection (highest distinct-serial ratio),
runs the duplicate scan, and prints a report. No external input required.

---

## Usage

### 1 — Standalone (default)

```
dotnet run -c Release -- "filtered mauro2.pcap"
```

Detects **SBUF** (same-buffer replay) and **ADJ** (adjacent replay) duplicate
events using the full ink-drop signature (P1Δ + P2Δ for all active pens).
These two checks have essentially zero false positives and need no external data.

### 2 — Known-duplicate investigation

Supply a comma-separated list of serials that external evidence (camera log,
downstream verification) confirms were physically duplicated:

```
dotnet run -c Release -- "capture.pcap" "SERIAL1,SERIAL2,SERIAL3"
```

Adds a **GLOBAL cross-buffer sweep** anchored to each known serial.
Finds HIGH-confidence displaced serials (both P1Δ and P2Δ match simultaneously
on a different buffer slot → head used the wrong buffer).

### 3 — Blank-OCR cross-reference

Supply serials where the camera read the datamatrix but got no OCR (text field
blank or unreadable), as the third argument:

```
dotnet run -c Release -- "capture.pcap" "" "SER_A,SER_B,SER_C"
```

Reports whether the blank OCR was caused by a protocol-level anomaly (P2 replay,
P2 zero-drop) or is purely a physical/optical issue.

### 4 — All modes combined

```
dotnet run -c Release -- "capture.pcap" "KNOWN_DUP_LIST" "BLANK_OCR_LIST"
```

---

## Report Sections

| # | Section | Description |
|---|---------|-------------|
| 1 | CONNECTIONS | Lists all TCP connections on ports 10000/10001; classifies test vs production |
| 2 | SUMMARY | Production print counts, active pens, time window |
| 3 | BUFFER MANAGEMENT | Buffer 0–31 cycle analysis, overlap events |
| 4 | SERIAL NUMBERS | Distinct-serial check in 0x55 command stream |
| 5 | **DUPLICATE SCAN** | **Core output — SBUF, ADJ (and GLOBAL if anchors provided)** |
| 6 | KNOWN-DUPLICATE INVESTIGATION | Only shown when arg 2 is provided |
| 7 | BLANK OCR / NO TEXT READ | Only shown when arg 3 is provided |
| 8 | ANOMALY SUMMARY | Counts of each anomaly type |

---

## Protocol Reference

### Physical setup
- **P1** — prints the **datamatrix** (2D barcode) — variable drop count per serial,
  but datamatrix module patterns can have coincidental equal total black-square counts.
- **P2** — prints the **human-readable text** (font) — more discriminating per-character
  drop count; collisions across different serials are rarer.
- The camera validates **both** pens on every label; a P1/P2 mismatch is flagged as
  an error. Silent duplicates therefore require **both** pens to replay the same old image.

### TCP ports
| Port | Direction | Purpose |
|------|-----------|---------|
| 10000 | Client → Printer | Commands |
| 10001 | Printer → Client | Notifications |

### Frame framing
```
STX(0x02) | FrameSize:uint32BE | 3 header bytes | payload | ETX(0x03)
Total frame = 5 + FrameSize bytes
```

### Command 0x55 (port 10000, client → printer)
Sends image data for a buffer slot.

| Byte offset | Field |
|-------------|-------|
| [7] | Command ID = 0x55 |
| [8] | BufferNumber (0–31) |
| [9–12] | PageNumber (uint32 BE) |
| [13] | Pulse1 |
| [14] | Pulse2 |
| [15–18] | DataBlockLength (uint32 BE) |
| [19…] | DataBlock — CR+LF delimited fields, FormFeed terminated |
| DataBlock field [12] (0-based) | **SerialNumber** |

### Notification 0x06 (port 10001, printer → client)
Signals end of print for one buffer.

| Byte offset | Field |
|-------------|-------|
| [7] | Notification ID = 0x06 |
| [8] | BufferNumber freed |
| [9] | QueueCount (buffers still pending) |
| [10–13] | PageNumber (uint32 BE, always 0) |
| [14–29] | 4× uint32 BE cumulative drop counters (P1, P2, P3, P4) |
| [30–33] | EncoderPulses cumulative (uint32 BE) |
| [34] | IsRealPrint (0 = init fill, 1 = production print) |
| [35–38] | PenStatus per pen (0=OK, 1=not-printed, 2=partial) |
| [39] | OverallStatus (0=OK, 1=partial, 2=sync-lost) |

### Buffer cycle
- Buffers cycle 0–31.
- First 30 notifications have `IsRealPrint=0` (init fills) — excluded from analysis.
- Buffer 31 has no notification during init fill.

### Duplicate detection logic

**Per-print delta** for pen P at print index i:
```
Δ[P][i] = DropsPerPen[P][i] − DropsPerPen[P][i−1]   (cumulative counters)
```

**Full signature** = (Δ[P1][i], Δ[P2][i], … for all active pens).

| Match type | Condition | Meaning |
|------------|-----------|---------|
| **SBUF** | signature[i] == signature[j], same buffer, j < i | Buffer slot not reloaded; old image replayed |
| **ADJ** | signature[i] == signature[i−1] | Head fired same image as immediately preceding print |
| **GLOBAL** | signature[i] == signature[j], any buffer, j < i | Head used a completely wrong buffer slot |

GLOBAL requires an external anchor (known-duplicate serial) to avoid false positives
from coincidental drop-count collisions across 10 000+ prints.

---

## Known failure modes identified in this capture

1. **Buffer not reloaded on same slot (SBUF)** — two events on buf=4:
   - `WKWKR3C3XCN896F` replayed → `EHKH2P94GM2HT43` displaced
   - `7XMF3MMCAVDG055` replayed → `RA0E0EE429CHT33` displaced

2. **Head printed adjacent buffer image (ADJ)** — one event on buf=22:
   - `6H0F8NWW20DCHCT` replayed → `3A5TGCE898CN05W` displaced

3. **Head used wrong buffer slot (GLOBAL)** — found only via known-duplicate
   investigation (both pens matched simultaneously on a different buffer):
   - `K41RP6F9X4D1TWP` → displaced `6G2704N24T4XCE7` (buf1) and `6160HVG81VTHE6T` (buf3)
   - `1XFVV33FNRW48ER` → displaced `T81HG6A54DTEKRR` (buf4) and `A66XXF4DW4ERR5D` (buf21)

---

## Copilot session reference

This tool was developed interactively with GitHub Copilot CLI.

**Session ID:** `3efe5585-f1af-4a39-8e1f-b2e99982ee68`

**Latest checkpoint:** `002-simplifying-tool-and-modernizi.md`

To resume the session and continue development (e.g., add support for new
protocol versions, additional analysis modes, or a different printer model),
open GitHub Copilot CLI in this repository and reference the session ID above.
The checkpoint contains the full conversation history, all technical decisions,
and the current state of the implementation.

### What the latest checkpoint covers
- Full duplicate detection logic (SBUF, ADJ, GLOBAL)
- Known-duplicate and blank-OCR investigation modes
- Code modernisation: `BinaryPrimitives`, `Span<byte>`/`ReadOnlySpan<byte>`, `ArrayPool<byte>`
- All findings from `filtered mauro2.pcap` (3 SBUF/ADJ events + 2 GLOBAL events)
