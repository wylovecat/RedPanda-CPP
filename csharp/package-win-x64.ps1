param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "src\RedPanda.Competition\RedPanda.Competition.csproj"
$project = [xml](Get-Content $csproj)
$version = ($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = "0.0.0" }

$distRoot = Join-Path $root "artifacts\package"
$dist = Join-Path $distRoot "RedPandaCP-$version-win-x64"
$zip = Join-Path $root "artifacts\RedPandaCP-$version-win-x64-portable.zip"
$setup = Join-Path $root "artifacts\RedPandaCP-$version-win-x64-setup.exe"
$icon = Join-Path $root "src\RedPanda.Competition\AppAssets\redpanda.ico"

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $dist -Force | Out-Null

dotnet publish (Join-Path $root "src\RedPanda.Competition\RedPanda.Competition.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $dist

Get-ChildItem -Path $dist -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
New-Item -ItemType File -Path (Join-Path $dist "portable.ini") -Force | Out-Null

$toolsDir = Join-Path $dist "tools\mingw64\bin"
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
Set-Content -Path (Join-Path $dist "tools\README.txt") -Encoding UTF8 -Value @'
将 MinGW-w64 解压到本目录，使 g++.exe 位于：
  tools\mingw64\bin\g++.exe

RedPanda CP 启动时会自动检测该位置，不需要修改 PATH 或设置。
'@

Set-Content -Path (Join-Path $dist "使用说明.txt") -Encoding UTF8 -Value @"
RedPanda CP 算法竞赛版 v$version

1. 双击 RedPandaCP.exe 启动。本目录含 portable.ini，设置和数据保存在 data 子目录。
2. 需要 g++。自动检测顺序：设置里指定 -> PATH -> 本目录 tools\mingw64\bin。
   可把 MinGW-w64 解压到 tools\mingw64。
3. 爱思创：登录后拉取班测/作业，导入题目；交题前勾选“允许在线提交”。
4. 快捷键：F9 编译运行，Ctrl+F9 只编译，F10 测试全部，Ctrl+J 代码片段。
5. 课堂计时：“计时”设置倒计时、暂停和交题锁定。
6. 日志：设置 -> 打开日志目录。

升级时保留 data 目录即可保留登录会话和提交记录。
"@

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dist -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Portable zip: $zip"

if (-not $SkipInstaller) {
    $nsis = Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"
    if (-not (Test-Path $nsis)) {
        $nsis = Join-Path $env:ProgramFiles "NSIS\makensis.exe"
    }

    if (Test-Path $nsis) {
        if (Test-Path $setup) { Remove-Item -Force $setup }
        $version4 = if ($version -match '^\d+\.\d+\.\d+\.\d+$') { $version } else { "$version.0" }
        & $nsis `
            "/DVERSION=$version" `
            "/DVERSION4=$version4" `
            "/DSOURCE=$(Join-Path $dist 'RedPandaCP.exe')" `
            "/DOUTFILE=$setup" `
            "/DAPPICON=$icon" `
            (Join-Path $root "installer\RedPandaCP.nsi")

        if ($LASTEXITCODE -ne 0) {
            throw "makensis failed with exit code $LASTEXITCODE"
        }

        Write-Host "Installer: $setup"
    }
    else {
        Write-Warning "NSIS makensis.exe not found; portable zip was still created."
    }
}

Write-Host ""
Write-Host "Version: $version"
Write-Host "Portable folder: $dist"
