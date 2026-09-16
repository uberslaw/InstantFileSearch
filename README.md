# Instant File Search

Windows disk explorer in the TreeSize mold: scan a folder, see the largest directories first, then type to find files instantly.

## What it does

- Scans a drive or folder in the background (reparse points — junctions and symlinks — skipped; access-denied paths ignored)
- Shows a folder tree sorted by size, with percent-of-parent bars. Each distinct scanned location is a top-level root (`this PC` or `network`) with that scan’s duration. Scanning the same path again replaces that root; different paths accumulate. Right-click a root → Remove scan. Each completed scan is appended to `%LocalAppData%\InstantFileSearch\logs\scans.log`.
- Lists files and subfolders for the selected directory
- Instant search across the scanned index (`*.log`, `report`, full path text; first 5,000 matches). Typing is debounced (200 ms) and filtered off the UI thread. **Advanced** (collapsed by default) adds size from/to (B–TB, 1024-based), modified from/to (`yyyy-MM-dd`), scope (all scans or the selected folder), and match (name, path, or both). Empty bounds mean no limit; size-only search is allowed. Filters apply to the in-memory index and are not saved.
- Restores the last successful scan from disk on launch (including when it ran); Scan again to refresh.
- Right-click a folder to exclude it from this view and future scans (`Scan` → Excluded folders… to undo)
- CLI for scan / search / status / exclude (same encrypted cache as the GUI)
- Open, Show in Explorer, copy path; drag a folder onto the window to scan it

## Run

Needs **Windows** and the **.NET 8 Desktop Runtime** (or the SDK). No network, no admin, no extra services.

```powershell
dotnet test tests/InstantFileSearch.Tests/InstantFileSearch.Tests.csproj
dotnet run --project src/InstantFileSearch/InstantFileSearch.csproj
dotnet run --project src/InstantFileSearch.Cli -- help
```

`InstantFileSearch.slnx` is for Visual Studio. `dotnet test InstantFileSearch.slnx` needs the .NET 9 SDK (SDK 8 cannot load `.slnx`).

Self-contained exe (no runtime install on the target PC):

```powershell
.\publish.ps1
```

Output is `dist\InstantFileSearch.exe` and `dist\InstantFileSearch.Cli.exe` (developer copy next to the repo; the CLI from this script is framework-dependent).

Portable folder for another PC (GUI + CLI, both self-contained single-file `win-x64`, compressed; no SDK, no obj/bin, no PDB, no runtime-pack DLL dump):

```powershell
.\scripts\PortablePublish.cmd
```

Pick a destination folder (folder picker, or `Read-Host` if the picker is unavailable). The script creates `InstantFileSearch\` there unless you already picked a folder with that name. Pass `-Destination D:\Apps` to skip the prompt (`-NonInteractive` refuses to guess `bin\` or `dist\`). Run this on **Windows** with the .NET 8 SDK; the packed folder then runs without installing the SDK or Desktop Runtime.


## Use

1. Browse or drop a folder
2. Scan
3. Click folders on the left (largest at the top)
4. Type in Search to filter files (`Ctrl+F`). Open **Advanced** for size, date, folder scope, and name vs path.
5. Close and reopen: the last scan and its time come back from `%LocalAppData%\InstantFileSearch`
6. Right-click a folder → Exclude folder to skip it next time

## CLI

The GUI is still the right place for the size tree. A console app covers the same scan/search/exclude/status work and shares the encrypted cache:

```powershell
dotnet run --project src/InstantFileSearch.Cli -- scan C:\Work
dotnet run --project src/InstantFileSearch.Cli -- search *.log
dotnet run --project src/InstantFileSearch.Cli -- status
dotnet run --project src/InstantFileSearch.Cli -- exclude add C:\Work\node_modules
dotnet run --project src/InstantFileSearch.Cli -- exclude list
```

Launch Control → Open CLI starts at the repo root with those commands in the banner.

Scanning `C:\` is allowed but slow and will skip folders you cannot read. Start with a project or user folder.

## Launch Control

This repo ships a Master Launch Control (MLC) plugin: a Standard WPF Launch Control (same chrome/theme as Switcheroo).

**Discovery.** MLC **Scan folder…** looks for `*LaunchControl*.cmd`. Register `scripts\InstantFileSearch-LaunchControl.cmd` (Adapter **Generic**). Sidecar `scripts\launch-control.json` gives the MLC card a PID for `InstantFileSearch.exe` when it exists. No MLC rebuild; Instant File Search is not in MLC’s first-run seed list.

**Start the LC.** Open Launch Control from the MLC card, or run `scripts\InstantFileSearch-LaunchControl.cmd`. The CMD builds `launch-control\InstantFileSearch.LaunchControl.exe` if needed, then starts it **without** `start ""` so an elevated MLC keeps its token.

The LC project references `LaunchControl.Standard` from [master-launch-control](https://github.com/uberslaw/master-launch-control). Clone that repo as a sibling of this one, at `%USERPROFILE%\Projects\master-launch-control`, or set `MLC_ROOT` / `LcStandard` to that clone. Linux/SDK builds of the LC project need `EnableWindowsTargeting` (already set in the csproj).

**Theme…** is LaunchControl.Standard. MLC discovers `lc-compat.json` next to the CMD first, then `launch-control\bin\{Release,Debug}\net8.0-windows\lc-compat.json`. This repo does **not** ship `scripts\lc-compat.json` (an unexpanded `%LOCALAPPDATA%` path would be used as a literal folder). Building the LC writes `themePath` expanded for the current user; running the LC also writes `%LOCALAPPDATA%\InstantFileSearch\theme.json` via `LcCompat.Write`.

| Button | What it does |
|--------|----------------|
| Start / Restart | Last Release or Debug config (default Release). Built `InstantFileSearch.exe` if present, else `dotnet run`. |
| Stop | Kills Instant File Search **from this repo** (bin / dist / a session this LC started). Not other apps. Closing the LC does not stop the app. |
| Refresh status | Running/Stopped, PID, last config. |
| Follow logs | Tails `%LOCALAPPDATA%\InstantFileSearch\logs\ops.log` (default OFF). |
| Rebuild Release / Debug | `dotnet build` of the WPF project; output streams into the pane. |
| Run Release / Debug | Sets last config and launches (refuses if already running). |
| Open CLI | `wt` if present, else `cmd.exe`, at the **repo root**. Log line says where it opened. |
| Publish | `publish.ps1` → `dist\InstantFileSearch.exe`. Portable pack is `scripts\PortablePublish.cmd` (folder picker), not an LC button — the picker cannot run in the LC redirected log pane. |
| Run tests | `dotnet test` on `tests\InstantFileSearch.Tests` (not the `.slnx`). |
| Open solution / project / dist / logs | Explorer (or the default app for `.slnx`). |

There is no Windows service and no Python/venv UI.

## Assumptions

- Runtime: Windows x64, .NET 8 Desktop (or the self-contained publish / portable pack)
- Network: none
- Permissions: read access to the folder you scan; no elevation required
- Data: the last successful scan is `%LocalAppData%\InstantFileSearch\last-scan.bin`, DPAPI-encrypted for the current Windows user (other local accounts cannot read it). Exclusions are `%LocalAppData%\InstantFileSearch\exclusions.bin`, same protection. It is a snapshot, not a live disk view. Administrators on the same machine can still access a logged-in user's DPAPI data.
