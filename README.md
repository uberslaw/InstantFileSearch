# Instant File Search

Windows disk explorer in the TreeSize mold: scan a folder, see the largest directories first, then type to find files instantly.

## What it does

- Scans a drive or folder in the background (junctions skipped, access-denied paths ignored)
- Shows a folder tree sorted by size, with percent-of-parent bars
- Lists files and subfolders for the selected directory
- Instant search across the scanned index (`*.log`, `report`, full path text)
- Open, Show in Explorer, copy path; drag a folder onto the window to scan it

## Run

Needs **Windows** and the **.NET 8 Desktop Runtime** (or the SDK). No network, no admin, no extra services.

```powershell
dotnet test InstantFileSearch.slnx
dotnet run --project src/InstantFileSearch/InstantFileSearch.csproj
```

Self-contained exe (no runtime install on the target PC):

```powershell
.\publish.ps1
```

Output is `dist\InstantFileSearch.exe`.

## Use

1. Browse or drop a folder
2. Scan
3. Click folders on the left (largest at the top)
4. Type in Search to filter files (`Ctrl+F`)

Scanning `C:\` is allowed but slow and will skip folders you cannot read. Start with a project or user folder.

## Assumptions

- Runtime: Windows x64, .NET 8 Desktop (or the self-contained publish)
- Network: none
- Permissions: read access to the folder you scan; no elevation required
- Data: nothing is stored; each scan is in-memory only
