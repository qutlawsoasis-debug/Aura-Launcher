# scripts/generate-splash.ps1
# Generates brand-aligned splash.png for Velopack installer based on DESIGN.md.
# Renders via a Chromium-based browser (Chrome or Edge, or CHROME_PATH override);
# temporary files stay in the system temp dir, never in the repository.
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$jsScript = Join-Path $scriptDir "generate-splash.js"

if (-not (Test-Path $jsScript)) {
    throw "generate-splash.js not found: $jsScript"
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "node.exe not found in PATH; it is required to run generate-splash.js"
}

& node $jsScript
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate splash.png (exit code $LASTEXITCODE)"
}
