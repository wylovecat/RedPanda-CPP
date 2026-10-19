param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipInstaller,
    [switch]$SkipFolderPublish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "src\RedPanda.Competition\RedPanda.Competition.csproj"
$project = [xml](Get-Content $csproj)
$version = ($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = "0.0.0" }

$artifacts = Join-Path $root "artifacts"
$distRoot = Join-Path $artifacts "package"
$dist = Join-Path $distRoot "RedPandaCP-$version-win-x64"
$folderDist = Join-Path $distRoot "RedPandaCP-$version-win-x64-folder"
$zip = Join-Path $artifacts "RedPandaCP-$version-win-x64-portable.zip"
$folderZip = Join-Path $artifacts "RedPandaCP-$version-win-x64-folder.zip"
$setup = Join-Path $artifacts "RedPandaCP-$version-win-x64-setup.exe"
$icon = Join-Path $root "src\RedPanda.Competition\AppAssets\redpanda.ico"

function Add-PortablePayload([string]$target) {
    New-Item -ItemType File -Path (Join-Path $target "portable.ini") -Force | Out-Null

    $toolsDir = Join-Path $target "tools\mingw64\bin"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    Set-Content -Path (Join-Path $target "tools\README.txt") -Encoding UTF8 -Value @'
将 MinGW-w64 解压到本目录，使 g++.exe 位于：
  tools\mingw64\bin\g++.exe

RedPanda CP 启动时会自动检测该位置，不需要修改 PATH 或设置。
'@

    $template = Join-Path $root "deploy\settings.example.json"
    if (Test-Path $template) {
        Copy-Item $template (Join-Path $target "settings.example.json") -Force
    }

    Set-Content -Path (Join-Path $target "使用说明.txt") -Encoding UTF8 -Value @"
RedPanda CP 算法竞赛版 v$version

一、启动
1. 双击 RedPandaCP.exe。本目录含 portable.ini，设置和数据保存在 data 子目录。
2. 需要 g++。自动检测顺序：设置里指定 -> PATH -> 本目录 tools\mingw64\bin。
   可把 MinGW-w64 解压到 tools\mingw64。

二、常用操作
1. 爱思创：登录后拉取班测/作业，导入题目；交题前勾选“允许在线提交”。
2. F9 编译运行，Ctrl+F9 只编译，F10 测试全部，Ctrl+J 代码片段。
3. 标签栏支持多文件；Ctrl+Tab 切换，Ctrl+W 关闭当前标签。
4. “对拍”可运行当前程序、数据生成器和暴力程序。
5. “计时”设置倒计时、暂停、交题锁定和任务锁定。
6. “成绩”按任务查看排名并导出 CSV；“错题本”汇总本机未 AC 的题目。
7. 日志：设置 -> 打开日志目录 / 导出诊断包。

三、机房部署
1. 便携版可直接复制整个目录，设置和登录会话都在 data 目录。
2. 预置配置：把 settings.example.json 复制为 data\settings.json 后修改。
3. 静默安装：RedPandaCP-$version-win-x64-setup.exe /S
   自定义目录：setup.exe /S /D=D:\RedPandaCP
4. 安装版配置在 %APPDATA%\RedPandaCP；卸载不会删除该目录。

升级时保留 data 目录即可保留登录会话、提交历史和片段。
"@
}

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $dist -Force | Out-Null

dotnet publish $csproj `
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
Add-PortablePayload $dist

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dist -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Portable zip: $zip"

if (-not $SkipFolderPublish) {
    if (Test-Path $folderDist) { Remove-Item -Recurse -Force $folderDist }
    New-Item -ItemType Directory -Path $folderDist -Force | Out-Null

    dotnet publish $csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $folderDist

    Get-ChildItem -Path $folderDist -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Add-PortablePayload $folderDist

    if (Test-Path $folderZip) { Remove-Item -Force $folderZip }
    Compress-Archive -Path $folderDist -DestinationPath $folderZip -CompressionLevel Optimal
    Write-Host "Folder zip: $folderZip"
}

if (-not $SkipInstaller) {
    $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
    $nsis = Join-Path $programFilesX86 "NSIS\makensis.exe"
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
        Write-Warning "NSIS makensis.exe not found; portable packages were still created."
    }
}

Write-Host ""
Write-Host "Version: $version"
Write-Host "Single-file folder: $dist"
if (-not $SkipFolderPublish) {
    Write-Host "Folder publish: $folderDist"
}
