$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\InstantFileSearch\InstantFileSearch.csproj"
$out = Join-Path $root "dist"

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem $out -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$exe = Join-Path $out "InstantFileSearch.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Publish finished but $exe was not created."
    exit 1
}

Write-Host "Published $exe"

$cliProject = Join-Path $root "src\InstantFileSearch.Cli\InstantFileSearch.Cli.csproj"
dotnet publish $cliProject -c Release -r win-x64 --self-contained false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem $out -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
$cli = Join-Path $out "InstantFileSearch.Cli.exe"
if (-not (Test-Path $cli)) {
    Write-Error "Publish finished but $cli was not created."
    exit 1
}
Write-Host "Published $cli"
