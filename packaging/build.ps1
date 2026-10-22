#Requires -Version 5.1
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"
$setupDir = Join-Path $root "artifacts"
$iscc = "D:\daily_tool\inno\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Install Inno Setup 6 and re-run."
}

Get-Process LyricClone -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir, $setupDir -Force | Out-Null

$project = Join-Path $root "src\LyricClone.App\LyricClone.App.csproj"
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit $LASTEXITCODE"
}

$iss = Join-Path $PSScriptRoot "LyricClone.iss"
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit $LASTEXITCODE"
}

Get-ChildItem $setupDir -Filter "lyric-clone-*-setup.exe" | ForEach-Object {
    Write-Host "SETUP=$($_.FullName)"
    Write-Host "SIZE=$($_.Length)"
}
