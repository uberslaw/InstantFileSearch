# Instant File Search portable pack.
# Publishes self-contained single-file win-x64 GUI + CLI into a prompted folder.
# Build machine needs the .NET 8 SDK. The destination PC does not.
[CmdletBinding()]
param(
    [string]$Destination,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$helpers = Join-Path $PSScriptRoot 'PortablePublish.Helpers.ps1'
if (-not (Test-Path -LiteralPath $helpers)) {
    throw "Missing helper script $helpers"
}
. $helpers

function Get-IfsRepoRoot {
    $root = Split-Path -Parent $PSScriptRoot
    $slnx = Join-Path $root 'InstantFileSearch.slnx'
    $wpf = Join-Path $root 'src/InstantFileSearch/InstantFileSearch.csproj'
    if (-not (Test-Path -LiteralPath $slnx) -or -not (Test-Path -LiteralPath $wpf)) {
        throw "Could not find Instant File Search repo root above $PSScriptRoot"
    }
    return $root
}

function Read-IfsDestinationFolder {
    param([string]$PromptText)

    $sta = [System.Threading.Thread]::CurrentThread.GetApartmentState()
    if ($sta -eq [System.Threading.ApartmentState]::STA) {
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = $PromptText
            $dialog.ShowNewFolderButton = $true
            $result = $dialog.ShowDialog()
            if ($result -eq [System.Windows.Forms.DialogResult]::OK -and -not [string]::IsNullOrWhiteSpace($dialog.SelectedPath)) {
                return $dialog.SelectedPath
            }
            if ($result -eq [System.Windows.Forms.DialogResult]::Cancel) {
                return $null
            }
        } catch {
            # COM picker next
        }
    }

    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.BrowseForFolder(0, $PromptText, 0x51)
        if ($folder -and $folder.Self -and -not [string]::IsNullOrWhiteSpace($folder.Self.Path)) {
            return $folder.Self.Path
        }
        return $null
    } catch {
        # Read-Host last
    }

    Write-Host $PromptText
    $typed = Read-Host 'Destination folder'
    if ([string]::IsNullOrWhiteSpace($typed)) { return $null }
    return $typed.Trim()
}

function Invoke-IfsDotnetPublish {
    param(
        [string]$ProjectPath,
        [string]$OutputDirectory,
        [string]$Label
    )
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Missing $Label project $ProjectPath"
    }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $publishArgs = Get-IfsPortableDotnetPublishArgumentList -ProjectPath $ProjectPath -OutputDirectory $OutputDirectory
    Write-Host "Publishing $Label (Release, win-x64, self-contained single-file)..."
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Label (exit $LASTEXITCODE)."
    }
}

function Clear-IfsPortableAppFolder {
    param([string]$AppFolder)
    if (-not (Test-Path -LiteralPath $AppFolder)) { return }
    Get-ChildItem -LiteralPath $AppFolder -Force | ForEach-Object {
        if ($_.PSIsContainer) {
            if ($_.Name.Equals('runtimes', [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
            return
        }
        if (Test-IfsPortableStaleFile $_.Name) {
            Remove-Item -LiteralPath $_.FullName -Force
        }
    }
}

function Copy-IfsPortablePublishOutput {
    param(
        [string]$PublishDirectory,
        [string]$AppFolder
    )
    $copied = New-Object System.Collections.Generic.List[string]
    $skipped = New-Object System.Collections.Generic.List[string]
    $natives = New-Object System.Collections.Generic.List[string]
    $nativeNames = @(Get-IfsPortableWpfNativeLeftoverFileNames)
    Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File -Force | ForEach-Object {
        $rel = $_.FullName.Substring($PublishDirectory.Length).TrimStart('\', '/')
        if (Test-IfsPortablePublishedFile $rel) {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $AppFolder $_.Name) -Force
            $copied.Add($_.Name) | Out-Null
            foreach ($native in $nativeNames) {
                if ($_.Name.Equals($native, [StringComparison]::OrdinalIgnoreCase)) {
                    $natives.Add($_.Name) | Out-Null
                }
            }
        } else {
            $skipped.Add($rel) | Out-Null
        }
    }
    [pscustomobject]@{
        Copied  = $copied
        Skipped = $skipped
        Natives = $natives
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET 8 SDK is required on this build machine (not on the PC that will run the portable folder).'
}

$root = Get-IfsRepoRoot
$wpfProject = Join-Path $root 'src/InstantFileSearch/InstantFileSearch.csproj'
$cliProject = Join-Path $root 'src/InstantFileSearch.Cli/InstantFileSearch.Cli.csproj'

$picked = $Destination
if ([string]::IsNullOrWhiteSpace($picked)) {
    if ($NonInteractive -or -not (Test-IfsPortableRunningOnWindows)) {
        throw 'Destination folder is required (folder picker is Windows-only; do not default to bin/ or dist/). The packed folder still only runs on Windows x64.'
    }
    $picked = Read-IfsDestinationFolder -PromptText 'Choose a folder for the Instant File Search portable copy (GUI + CLI). An InstantFileSearch subfolder is created unless you pick one with that name.'
}
if ([string]::IsNullOrWhiteSpace($picked)) {
    throw 'No destination folder selected. Refusing to dump into the repo (bin/, dist/, or otherwise).'
}

$destinationFull = [System.IO.Path]::GetFullPath($picked)
if (-not (Test-Path -LiteralPath $destinationFull)) {
    New-Item -ItemType Directory -Force -Path $destinationFull | Out-Null
    $destinationFull = [System.IO.Path]::GetFullPath($destinationFull)
}
if (-not (Test-Path -LiteralPath $destinationFull -PathType Container)) {
    throw "Destination is not a folder: $destinationFull"
}

$appFolder = Resolve-IfsPortableAppFolder -Destination $destinationFull
if (Test-IfsPathUnderRoot -Path $appFolder -Root $root) {
    Write-Host "Warning: packing into the repo at $appFolder. The portable folder is meant to be copied elsewhere; this is allowed only because the path was chosen explicitly."
    if (Test-IfsWellKnownRepoDump -RepoRoot $root -Path $appFolder) {
        Write-Host "Warning: $appFolder is a well-known build dump (bin/obj/dist/build). Owned publish files there will be replaced."
    }
}

New-Item -ItemType Directory -Force -Path $appFolder | Out-Null
Clear-IfsPortableAppFolder -AppFolder $appFolder

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ifs-portable-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
try {
    $uiOut = Join-Path $tempRoot 'ui'
    $cliOut = Join-Path $tempRoot 'cli'
    Invoke-IfsDotnetPublish -ProjectPath $wpfProject -OutputDirectory $uiOut -Label 'Instant File Search (WPF)'
    Invoke-IfsDotnetPublish -ProjectPath $cliProject -OutputDirectory $cliOut -Label 'InstantFileSearch.Cli'

    $uiCopy = Copy-IfsPortablePublishOutput -PublishDirectory $uiOut -AppFolder $appFolder
    $cliCopy = Copy-IfsPortablePublishOutput -PublishDirectory $cliOut -AppFolder $appFolder
    $extraNatives = @($uiCopy.Natives | Select-Object -Unique)

    foreach ($exe in Get-IfsPortableExpectedExeNames) {
        $path = Join-Path $appFolder $exe
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Publish finished but $path was not copied. Single-file output was missing from the temp publish folder."
        }
    }

    $readmePath = Join-Path $appFolder (Get-IfsPortableReadmeFileName)
    Set-Content -LiteralPath $readmePath -Value (Get-IfsPortableReadmeText -ExtraNativeFiles $extraNatives) -Encoding utf8

    $skipped = @($uiCopy.Skipped + $cliCopy.Skipped)
    $baggage = @($skipped | Where-Object { Test-IfsPortableRuntimeBaggage $_ })
    if ($baggage.Count -gt 20) {
        throw "dotnet publish left $($baggage.Count) runtime-pack files in the temp folder. Single-file bundling did not take effect; refusing to copy that sea of DLLs."
    }
    if ($skipped.Count -gt 0) {
        Write-Host ("Skipped {0} publish leftover(s) (pdb/xml/deps/runtime pack). First: {1}" -f $skipped.Count, $skipped[0])
    }
    if ($extraNatives.Count -gt 0) {
        Write-Host ("Included WPF native leftovers (IncludeNativeLibrariesForSelfExtract did not swallow them): {0}" -f ($extraNatives -join ', '))
    }

    Write-Host "Portable folder: $appFolder"
    foreach ($exe in Get-IfsPortableExpectedExeNames) {
        Write-Host ("  " + (Join-Path $appFolder $exe))
    }
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
