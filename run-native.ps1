$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$targetFramework = "net10.0-windows10.0.26100.0"
$exePath = Join-Path $projectDirectory "bin/Release/$targetFramework/HimeMikotoDesktopNative.exe"
$projectPath = Join-Path $projectDirectory "HimeMikotoDesktopNative.csproj"
$bundlePath = Join-Path $projectDirectory "web/mmd.bundle.js"
$physicsOutputPath = Join-Path $projectDirectory "web/mmd-spr.wasm"
$runtimeSourceDirectory = Join-Path $projectDirectory "mmd-runtime/src"

$needsBundle = -not (Test-Path -LiteralPath $bundlePath) -or -not (Test-Path -LiteralPath $physicsOutputPath)
if (-not $needsBundle) {
    $bundleWriteTime = (Get-Item -LiteralPath $bundlePath).LastWriteTimeUtc
    $runtimeSources = Get-ChildItem -LiteralPath $runtimeSourceDirectory -Recurse -File
    $needsBundle = $runtimeSources | Where-Object { $_.LastWriteTimeUtc -gt $bundleWriteTime } | Select-Object -First 1
    $needsBundle = $null -ne $needsBundle
}

if ($needsBundle) {
    $bundleScript = Join-Path $projectDirectory "mmd-runtime/build.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bundleScript
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$needsBuild = -not (Test-Path -LiteralPath $exePath)
if (-not $needsBuild) {
    $binaryWriteTime = (Get-Item -LiteralPath $exePath).LastWriteTimeUtc
    $sourceFiles = Get-ChildItem -LiteralPath $projectDirectory -Recurse -File |
        Where-Object {
            $_.FullName -notmatch "\\(bin|obj)\\" -and
            $_.Extension -in @(".cs", ".xaml", ".csproj", ".props", ".targets", ".resx")
        }
    $needsBuild = $sourceFiles | Where-Object { $_.LastWriteTimeUtc -gt $binaryWriteTime } | Select-Object -First 1
    $needsBuild = $null -ne $needsBuild
}

if ($needsBuild) {
    $dotnet = "C:/Program Files/dotnet/dotnet.exe"
    & $dotnet build $projectPath --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Start-Process -FilePath $exePath -WorkingDirectory $projectDirectory -WindowStyle Hidden
