$ErrorActionPreference = "Stop"

$runtimeDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectDirectory = Split-Path -Parent $runtimeDirectory
$esbuild = Join-Path $runtimeDirectory "node_modules/.pnpm/@esbuild+win32-x64@0.25.10/node_modules/@esbuild/win32-x64/esbuild.exe"
$entry = Join-Path $runtimeDirectory "src/mmd-runtime.js"
$output = Join-Path $projectDirectory "web/mmd.bundle.js"
$physicsWasm = Join-Path $runtimeDirectory "node_modules/babylon-mmd/esm/Runtime/Optimized/wasm/spr/index_bg.wasm"
$physicsOutput = Join-Path $projectDirectory "web/mmd-spr.wasm"

if (-not (Test-Path -LiteralPath $esbuild)) {
    throw "The bundled esbuild executable was not found: $esbuild"
}
if (-not (Test-Path -LiteralPath $physicsWasm)) {
    throw "The integrated MMD physics binary was not found: $physicsWasm"
}

& $esbuild $entry --bundle --format=iife --platform=browser --target=es2022 --outfile=$output --sourcemap
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item -LiteralPath $physicsWasm -Destination $physicsOutput -Force
