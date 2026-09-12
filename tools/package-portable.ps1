param(
    [string]$OutputDirectory = "",
    [switch]$IncludeLocalPrivateAssets
)

$ErrorActionPreference = "Stop"

$toolDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectDirectory = Split-Path -Parent $toolDirectory
$repositoryDirectory = Split-Path -Parent $projectDirectory
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectDirectory "dist/portable"
}

$bundlePath = Join-Path $projectDirectory "web/mmd.bundle.js"
$physicsOutputPath = Join-Path $projectDirectory "web/mmd-spr.wasm"
if (-not (Test-Path -LiteralPath $bundlePath) -or -not (Test-Path -LiteralPath $physicsOutputPath)) {
    $bundleScript = Join-Path $projectDirectory "mmd-runtime/build.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bundleScript
    if ($LASTEXITCODE -ne 0) {
        throw "The browser runtime bundle could not be built."
    }
}

$dotnet = "C:/Program Files/dotnet/dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "The .NET SDK was not found."
    }
    $dotnet = $dotnetCommand.Source
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$projectPath = Join-Path $projectDirectory "HimeMikotoDesktopNative.csproj"
& $dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $OutputDirectory `
    --no-restore `
    --nologo `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "The self-contained desktop pet publish failed."
}

$packageAssetsDirectory = Join-Path $OutputDirectory "assets"
$creditsSource = Join-Path $projectDirectory "assets/motions/MOTION-CREDITS.md"
$creditsDestination = Join-Path $packageAssetsDirectory "motions/MOTION-CREDITS.md"
New-Item -ItemType Directory -Path (Split-Path -Parent $creditsDestination) -Force | Out-Null
Copy-Item -LiteralPath $creditsSource -Destination $creditsDestination -Force

if ($IncludeLocalPrivateAssets) {
    $modelsSource = Join-Path $repositoryDirectory "HimeMikotoDesktop/assets/Hime_&_Mikoto"
    $modelsDestination = Join-Path $packageAssetsDirectory "Hime_&_Mikoto"
    if (-not (Test-Path -LiteralPath $modelsSource)) {
        throw "The local model directory was not found: $modelsSource"
    }
    Copy-Item -LiteralPath $modelsSource -Destination $modelsDestination -Recurse -Force

    $motionsSource = Join-Path $projectDirectory "assets/motions"
    $motionsDestination = Join-Path $packageAssetsDirectory "motions"
    New-Item -ItemType Directory -Path $motionsDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $motionsSource -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $motionsDestination $_.Name) -Recurse -Force
    }

    $musicSource = Join-Path $projectDirectory "assets/music"
    if (Test-Path -LiteralPath $musicSource) {
        Copy-Item -LiteralPath $musicSource -Destination (Join-Path $packageAssetsDirectory "music") -Recurse -Force
    }
}

$packageReadme = @"
Hime & Mikoto Desktop Pet

双击 HimeMikotoDesktopNative.exe 即可启动桌宠。
Double-click HimeMikotoDesktopNative.exe to start.

这是便携版：不要只复制 exe，请保留整个文件夹。
This is a portable build: keep the whole folder together.

如果程序提示缺少 WebView2，请安装 Microsoft Edge WebView2 Runtime 后重试。
If the app reports that WebView2 is missing, install the Microsoft Edge WebView2 Runtime and try again.

本包是否包含人物模型和动作，取决于打包时的选项。第三方素材仍受原始条款约束。
The package may contain local character models and motions depending on the packaging option. Third-party assets remain subject to their original terms.
"@
Set-Content -LiteralPath (Join-Path $OutputDirectory "使用说明.txt") -Value $packageReadme -Encoding utf8

$zipPath = "$OutputDirectory.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $OutputDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output "Portable folder: $OutputDirectory"
Write-Output "Portable zip: $zipPath"
