# PingViewScan for Windows

A native Windows port of the [macOS PingViewScan app](https://github.com/bejuwt/pingviewscan):
a multi-host ICMP monitor, an IP-block network scanner, an IPv4 subnet calculator and
a graphical traceroute tool, with self-contained HTML/SVG and CSV reports. Built with
C# / .NET 10 and WPF.

*Tabs: Ping Monitor · Network Scan · Subnet Calculator · Traceroute*

## Download

Grab a prebuilt, self-contained executable from the
[Releases](https://github.com/bejuwt/windows-pingviewscan/releases) page
(`PingViewScan.App.exe` or the `.zip`). No .NET install required.

> The binaries are **not code-signed**, so Windows SmartScreen may show
> "Windows protected your PC". Choose **More info → Run anyway**, or
> right-click the file → **Properties → Unblock**.

## Features

**Ping Monitor**
- Track any number of hosts (names or IPs); add them one at a time or paste a
  whole column straight out of Excel.
- Sortable live table: Host, IP, Hostname (reverse DNS), Sent / Recv / Lost,
  Loss %, Last, Min, Max, Avg.
- Live latency graph with a selectable timeframe (5/10/30/60 minutes or the whole
  session) and per-host filtering. Ctrl/Shift-click rows in the table, then pick
  **Selected** to graph only those hosts.
- Switch the graph to cumulative **packet loss** to see loss build up over time.
- Configurable interval and per-probe timeout; Start/Stop at any time.
- Export a self-contained **HTML report** (latency + packet-loss SVG charts) or a
  raw **CSV** of every probe.

**Network Scan**
- Expand an IP block expression: `10.0.0.0/24`, `10.0.0.1-50`, `10.0.0.1-10.0.0.20`,
  `10.0.0.*`, or a comma-separated list.
- ICMP discovery plus TCP port scanning with selectable presets (Common services,
  Web, Windows/AD, Infrastructure, Remote access) or a custom port list/ranges.
- Results table with status, latency and open ports; export an **HTML report**
  (subnet occupancy map, latency bars, most-common-ports chart, full results) or
  **CSV**.

**Subnet Calculator**
- Type an address as `192.168.1.10/24` (or pick a prefix) and see the network,
  broadcast, mask and wildcard mask, usable host range and address counts.
- Shows the IP class, scope (private, loopback, link-local, CGNAT, documentation,
  multicast, public, …) and binary views of the address and mask.
- One-click **Copy Summary** for pasting into tickets or chat.

**Traceroute**
- Graphical traceroute with a live source → hop → target path, colour-coded by
  round-trip latency. Works with the built-in Windows `tracert`.
- Choose max hops, probes per hop and timeout; **Continuous** mode repeats the
  trace every few seconds and aggregates min/avg/max latency and loss per hop
  across every pass.
- Hop table with sent / received / lost, loss %, last, min, avg and max.
- Export a self-contained **HTML report** (SVG hop-path diagram, min/avg/max
  latency chart, per-hop table and continuous-mode aggregates) or **CSV**.

All reports are single, dependency-free HTML files that open in any browser.

## Requirements

- **To run a published build:** 64-bit Windows 10/11. The self-contained publish
  includes the .NET runtime, so nothing else is needed.
- **To build from source:** the [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Build & run

```powershell
dotnet build PingViewScan.slnx
dotnet run --project src/PingViewScan.App
```

Produce a standalone executable:

```powershell
./publish.ps1                 # self-contained single-file win-x64
./publish.ps1 -FrameworkDependent   # smaller, requires the .NET 10 desktop runtime
```

The executable lands in `src/PingViewScan.App/bin/Release/net10.0-windows/win-x64/publish/`.

## Command-line (headless)

The `PingViewScan.Cli` project exercises the same engine without a GUI — handy for
scripting and for verifying behaviour on non-Windows hosts:

```powershell
dotnet run --project src/PingViewScan.Cli -- --hosts 10.0.0.1,10.0.0.2 --duration 10 --report out.html --csv out.csv
dotnet run --project src/PingViewScan.Cli -- --scan 10.0.0.0/24 --ports 22,80,443,8000-8010 --report scan.html
dotnet run --project src/PingViewScan.Cli -- --trace 1.1.1.1 --report trace.html --csv trace.csv
dotnet run --project src/PingViewScan.Cli -- --subnet 192.168.1.10/24
```

`--trace` accepts `--max-hops`, `--queries`, `--trace-timeout`, `--rounds` and
`--interval` for continuous multi-pass traces. `--subnet` accepts an optional
`--prefix` when the address has no `/prefix` suffix.

## Project layout

- `src/PingViewScan.Core` — platform-agnostic engine: models, `IPRange`,
  `PingRunner` (spawns the OS `ping`, never a shell), `PortScanner`, DNS helpers,
  `HostImport`, `SubnetCalculator`, `TracerouteRunner`, chart sampling, SVG chart
  generation and the HTML/CSV exporters.
- `src/PingViewScan.App` — WPF UI (Monitor, Scan, Subnet Calculator and Traceroute
  tabs), view models, custom chart renderers and the bulk-add dialog.
- `src/PingViewScan.Cli` — headless console front-end.

## Security notes

- Hosts are always passed to `ping` and traceroute as dedicated arguments (no
  shell), and values beginning with `-` are rejected to prevent option injection.
- CSV exports prefix cells starting with `= + - @` with a quote to neutralise
  spreadsheet formula injection.
- All user/DNS-controlled strings are HTML-escaped in the reports.

**Only scan networks you are authorised to scan.** The port scanner is a dual-use
tool.
