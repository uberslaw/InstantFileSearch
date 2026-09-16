# Portable pack contract. Keep in sync with tests/InstantFileSearch.Tests/PortablePublishLayout.cs.
# Single-file self-contained win-x64; no trim; no ReadyToRun; no runtime-pack DLL dump.

function Get-IfsPortableRuntimeIdentifier { 'win-x64' }
function Get-IfsPortableConfiguration { 'Release' }
function Get-IfsPortableAppFolderName { 'InstantFileSearch' }
function Get-IfsPortableReadmeFileName { 'README.txt' }

function Get-IfsPortableExpectedExeNames {
    @('InstantFileSearch.exe', 'InstantFileSearch.Cli.exe')
}

function Get-IfsPortableWpfNativeLeftoverFileNames {
    @(
        'D3DCompiler_47_cor3.dll',
        'PenImc_cor3.dll',
        'PresentationNative_cor3.dll',
        'vcruntime140_cor3.dll',
        'wpfgfx_cor3.dll'
    )
}

function Get-IfsPortableRuntimeBaggageFileNames {
    @(
        'coreclr.dll',
        'clrjit.dll',
        'clretwrc.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'mscordaccore.dll',
        'createdump.exe'
    )
}

function Get-IfsPortableDotnetPublishArgumentList {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )
    @(
        'publish'
        $ProjectPath
        '-c', 'Release'
        '-r', 'win-x64'
        '--self-contained', 'true'
        '-p:PublishSingleFile=true'
        '-p:EnableCompressionInSingleFile=true'
        '-p:IncludeNativeLibrariesForSelfExtract=true'
        '-p:DebugType=None'
        '-p:DebugSymbols=false'
        '-o', $OutputDirectory
    )
}

function Get-IfsPortableFileName {
    param([string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return '' }
    return [System.IO.Path]::GetFileName($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
}

function Test-IfsPortableRuntimesFolder {
    param([string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $false }
    $normalized = $RelativePath.Replace('\', '/').TrimStart('/')
    return $normalized -eq 'runtimes' -or $normalized.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase)
}

function Test-IfsPortableRuntimeBaggage {
    param([string]$RelativePath)
    $name = Get-IfsPortableFileName $RelativePath
    if ([string]::IsNullOrWhiteSpace($name)) { return $false }
    foreach ($candidate in Get-IfsPortableRuntimeBaggageFileNames) {
        if ($name.Equals($candidate, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    if ($name.StartsWith('mscordaccore', [StringComparison]::OrdinalIgnoreCase) -and $name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    if ($name.StartsWith('Microsoft.DiaSymReader.Native', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    return Test-IfsPortableRuntimesFolder $RelativePath
}

function Test-IfsPortablePublishedFile {
    param([string]$RelativePath)
    $name = Get-IfsPortableFileName $RelativePath
    if ([string]::IsNullOrWhiteSpace($name)) { return $false }
    if ($name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.xml', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.xaml', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($name.EndsWith('.exe.config', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if (Test-IfsPortableRuntimeBaggage $RelativePath) { return $false }
    if (Test-IfsPortableRuntimesFolder $RelativePath) { return $false }
    foreach ($exe in Get-IfsPortableExpectedExeNames) {
        if ($name.Equals($exe, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    foreach ($native in Get-IfsPortableWpfNativeLeftoverFileNames) {
        if ($name.Equals($native, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-IfsPortableStaleFile {
    param([string]$RelativePath)
    $name = Get-IfsPortableFileName $RelativePath
    if ([string]::IsNullOrWhiteSpace($name)) { return $false }
    if (Test-IfsPortablePublishedFile $name) { return $true }
    if (Test-IfsPortableRuntimeBaggage $name) { return $true }
    if ($name.Equals((Get-IfsPortableReadmeFileName), [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($name.EndsWith('.xml', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($name.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    $ext = [System.IO.Path]::GetExtension($name)
    return $ext.Equals('.dll', [StringComparison]::OrdinalIgnoreCase) `
        -or $ext.Equals('.exe', [StringComparison]::OrdinalIgnoreCase) `
        -or $ext.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-IfsPortableAppFolder {
    param([Parameter(Mandatory = $true)][string]$Destination)
    if ([string]::IsNullOrWhiteSpace($Destination)) {
        throw 'Destination folder is required; do not default to bin/ or dist/.'
    }
    $full = [System.IO.Path]::GetFullPath($Destination)
    $trimmed = $full.TrimEnd([char[]]@('\', '/'))
    $leaf = [System.IO.Path]::GetFileName($trimmed)
    if ($leaf.Equals((Get-IfsPortableAppFolderName), [StringComparison]::OrdinalIgnoreCase)) {
        return $trimmed
    }
    return Join-Path $trimmed (Get-IfsPortableAppFolderName)
}

function Test-IfsPathUnderRoot {
    param(
        [string]$Path,
        [string]$Root
    )
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Root)) { return $false }
    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
        $fullRoot = [System.IO.Path]::GetFullPath($Root)
    } catch {
        return $false
    }
    $prefix = $fullRoot.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    $cmpRoot = $fullRoot.TrimEnd([char[]]@('\', '/'))
    if ($fullPath.Equals($cmpRoot, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Test-IfsWellKnownRepoDump {
    param(
        [string]$RepoRoot,
        [string]$Path
    )
    if ([string]::IsNullOrWhiteSpace($RepoRoot) -or [string]::IsNullOrWhiteSpace($Path)) { return $false }
    foreach ($name in @('bin', 'obj', 'dist', 'build')) {
        if (Test-IfsPathUnderRoot -Path $Path -Root (Join-Path $RepoRoot $name)) { return $true }
    }
    return $false
}

function Get-IfsPortableReadmeText {
    param([string[]]$ExtraNativeFiles)
    $nativeNote = if ($ExtraNativeFiles -and $ExtraNativeFiles.Count -gt 0) {
        'WPF native libraries could not be fully bundled, so these files sit next to the GUI exe and are required: ' + ($ExtraNativeFiles -join ', ') + '.'
    } else {
        'Native WPF libraries are bundled inside InstantFileSearch.exe (IncludeNativeLibrariesForSelfExtract) and extract under %TEMP%\.net on first run.'
    }

    @"
Instant File Search (portable)

This folder is a self-contained Windows x64 copy of Instant File Search
and InstantFileSearch.Cli. It is not a .NET SDK tree: no source, no obj/bin,
no runtime-pack DLL dump.

You do not need the .NET SDK or the .NET 8 Desktop Runtime installed on
the PC that runs these executables. You do need Windows 10/11 x64.

Run:
  InstantFileSearch.exe        GUI
  InstantFileSearch.Cli.exe    scan / search / status / exclude

Settings and the last scan live in %LOCALAPPDATA%\InstantFileSearch
(not in this folder). Copying this folder does not copy your scans.

$nativeNote

Rebuild this pack from a machine with the .NET 8 SDK:
  scripts\PortablePublish.cmd
"@
}

function Test-IfsPortableRunningOnWindows {
    $isWindowsVar = Get-Variable -Name IsWindows -ErrorAction SilentlyContinue
    if ($null -ne $isWindowsVar) {
        return [bool]$isWindowsVar.Value
    }
    return $env:OS -eq 'Windows_NT'
}
