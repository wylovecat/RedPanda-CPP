param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Output = "$PSScriptRoot\artifacts\win-x64"
)

$ErrorActionPreference = "Stop"

dotnet publish "$PSScriptRoot\src\RedPanda.Competition\RedPanda.Competition.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $Output

Get-ChildItem -Path $Output -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

$portableMarker = Join-Path $Output "portable.ini"
if (-not (Test-Path $portableMarker)) {
    New-Item -ItemType File -Path $portableMarker | Out-Null
}

Write-Host ""
Write-Host "Output: $Output\RedPandaCP.exe"
Write-Host "portable.ini included; copy the whole folder for portable mode."
