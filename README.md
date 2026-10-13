# PingViewScan for Windows

A native Windows port of the [macOS PingViewScan app](../pingviewscan): a multi-host
ICMP monitor and an IP-block network scanner, with self-contained HTML/SVG and CSV
reports. Built with C# / .NET 10 and WPF.

![tabs: Ping Monitor, Network Scan]

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
- Live table: Host, IP, Hostname (reverse DNS), Sent / Recv / Lost, Loss %, Last,
  Min, Max, Avg.
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

Both reports are single, dependency-free HTML files that open in any browser.

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
```

## Project layout

- `src/PingViewScan.Core` — platform-agnostic engine: models, `IPRange`, `PingRunner`
  (spawns the OS `ping`, never a shell), `PortScanner`, DNS helpers, `HostImport`,
  SVG chart generation and the HTML/CSV exporters.
- `src/PingViewScan.App` — WPF UI (Monitor + Scan tabs), view models and the bulk-add dialog.
- `src/PingViewScan.Cli` — headless console front-end.

## Security notes

- Hosts are always passed to `ping` as a dedicated argument (no shell), and values
  beginning with `-` are rejected to prevent option injection.
- CSV exports prefix cells starting with `= + - @` with a quote to neutralise
  spreadsheet formula injection.
- All user/DNS-controlled strings are HTML-escaped in the reports.

**Only scan networks you are authorised to scan.** The port scanner is a dual-use
tool.
