# scripts/build-release.ps1
# Safe, isolated release packaging for Aura Launcher (Velopack / vpk).
#
# Usage:
#   Plan / validation only (default, zero side effects):
#     powershell -NoProfile -File scripts\build-release.ps1 -OutDir "$env:TEMP\AuraDev\build\release"
#   Real packaging (explicit order required, e.g. for the Releaser role):
#     powershell -NoProfile -File scripts\build-release.ps1 -OutDir "<dir>" -Version 1.2.153 -UpdateVersionFiles -Execute
#   Optional delta support:
#     -PreviousPackagesDir "<dir with previous AuraLauncher-*.nupkg, RELEASES, releases.win.json>"
#
# Safety contract (per AGENTS.md and the isolation audit):
#   - -OutDir is MANDATORY. There is no default output directory and C:\AuraRelease
#     is never referenced by this script.
#   - Without -Execute the script only validates and prints the plan: no builds,
#     no vpk, no version.json / AuraLauncher.csproj changes, no git operations,
#     nothing written to the user profile.
#   - version.json / AuraLauncher.csproj are only rewritten with -UpdateVersionFiles
#     AND -Execute.
#   - The script NEVER deletes existing versions or sibling directories; the only
#     directory it removes is its own temp folder under %TEMP%\AuraDev\release\<guid>.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutDir,

    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [switch]$Increment,

    # Optional directory with previous release packages (*.nupkg, RELEASES,
    # releases.win.json). Matching files are COPIED into -OutDir before vpk pack
    # so delta updates can be generated. Nothing is ever deleted from it.
    [Parameter(Mandatory = $false)]
    [string]$PreviousPackagesDir = "",

    # Persist the resolved version into version.json and AuraLauncher.csproj.
    # Requires -Execute; without it the script never touches those files.
    [Parameter(Mandatory = $false)]
    [switch]$UpdateVersionFiles,

    # Actually run dotnet publish + vpk pack. Default is plan-only.
    [Parameter(Mandatory = $false)]
    [switch]$Execute
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "AuraLauncher.csproj"))) {
    $repoRoot = (Get-Location).Path
}

# ---------------------------------------------------------------------------
# 1. Version resolution (read-only; files are changed only with
#    -UpdateVersionFiles -Execute)
# ---------------------------------------------------------------------------
$versionJsonPath = Join-Path $repoRoot "version.json"
$csprojPath = Join-Path $repoRoot "AuraLauncher.csproj"
$internalVersion = "1.2.9"
$userFacingVersion = "beta 1.0.1"
$changelog = @()

if (Test-Path $versionJsonPath) {
    $vData = [System.IO.File]::ReadAllText($versionJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $internalVersion = $vData.internalVersion
    $userFacingVersion = $vData.userFacingVersion
    if ($null -ne $vData.changelog) {
        $changelog = @($vData.changelog)
    }
}

if ($Increment -and -not [string]::IsNullOrWhiteSpace($Version)) {
    throw "Specify either -Version or -Increment, not both."
}

if ($Increment) {
    $parts = $internalVersion.Split('.')
    if ($parts.Length -eq 3) {
        $patch = [int]$parts[2] + 1
        $internalVersion = "$($parts[0]).$($parts[1]).$patch"
        $betaNum = $patch - 8
        $userFacingVersion = "beta 1.0.$betaNum"
    }
} elseif (-not [string]::IsNullOrWhiteSpace($Version)) {
    $internalVersion = $Version.TrimStart('v')
    $parts = $internalVersion.Split('.')
    if ($parts.Length -eq 3) {
        $betaNum = [int]$parts[2] - 8
        $userFacingVersion = "beta 1.0.$betaNum"
    }
}

# ---------------------------------------------------------------------------
# 2. -OutDir safety validation (explicit external directory only)
# ---------------------------------------------------------------------------
function Test-IsUnderPath([string]$Child, [string]$Parent) {
    $c = [System.IO.Path]::GetFullPath($Child).TrimEnd('\') + '\'
    $p = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    return $c.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)
}

$resolvedOutDir = [System.IO.Path]::GetFullPath($OutDir)

if ($resolvedOutDir -match '^[A-Za-z]:\\?$') {
    throw "Refusing to use a drive root as -OutDir: $resolvedOutDir"
}
if (Test-IsUnderPath $resolvedOutDir $repoRoot) {
    throw "-OutDir must be outside the repository: $resolvedOutDir"
}
if (Test-IsUnderPath $resolvedOutDir $env:windir) {
    throw "-OutDir must not be inside the Windows directory: $resolvedOutDir"
}
foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432)) {
    if ($pf -and (Test-IsUnderPath $resolvedOutDir $pf)) {
        throw "-OutDir must not be inside Program Files: $resolvedOutDir"
    }
}
if (Test-IsUnderPath $resolvedOutDir $env:APPDATA) {
    throw "-OutDir must not be inside the roaming profile (APPDATA): $resolvedOutDir"
}
if ((Test-IsUnderPath $resolvedOutDir $env:LOCALAPPDATA) -and -not (Test-IsUnderPath $resolvedOutDir $env:TEMP)) {
    throw "-OutDir inside LOCALAPPDATA is only allowed under TEMP (protects installed launcher, WebView2 and .aura profiles): $resolvedOutDir"
}
if ((Test-Path $resolvedOutDir) -and (-not (Test-Path $resolvedOutDir -PathType Container))) {
    throw "-OutDir exists and is not a directory: $resolvedOutDir"
}

# ---------------------------------------------------------------------------
# 3. Resolve installer assets (icon, splash)
# ---------------------------------------------------------------------------
$iconPath = Join-Path $repoRoot "Resources\aura-icon.ico"
if (-not (Test-Path $iconPath)) {
    $iconPath = Join-Path $repoRoot "app_icon.ico"
}
if (-not (Test-Path $iconPath)) {
    throw "Installer icon not found (Resources\aura-icon.ico / app_icon.ico)"
}

$splashPath = Join-Path $repoRoot "splash.png"
$splashFound = Test-Path $splashPath
# Brand accent from DESIGN.md; verified against `vpk pack --help` (vpk 1.2.161):
# --splashProgressColor <COLOR>  "Progress bar color (e.g. #FF0000), or 'None' to hide."
$splashProgressColor = "#F2A63C"

if (-not $splashFound) {
    Write-Warning "splash.png not found at $splashPath - the installer would fall back to the plain Velopack splash with the default green progress bar."
}

# ---------------------------------------------------------------------------
# 4. Command plan
# ---------------------------------------------------------------------------
$taskTempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("AuraDev\release\" + [System.Guid]::NewGuid().ToString("N"))
$tempPublishDir = Join-Path $taskTempRoot "publish"
$tempObjDir = Join-Path $taskTempRoot "obj\"
$tempBinDir = Join-Path $taskTempRoot "bin\"

$publishArgs = @(
    "publish", $csprojPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:Version=$internalVersion",
    "-p:BaseIntermediateOutputPath=$tempObjDir",
    "-p:BaseOutputPath=$tempBinDir",
    "-o", $tempPublishDir
)

$vpkArgs = @(
    "vpk", "pack",
    "-u", "AuraLauncher",
    "-v", $internalVersion,
    "-p", $tempPublishDir,
    "-e", "AuraLauncher.exe",
    "--packTitle", "Aura",
    "-i", $iconPath,
    "-r", "win-x64",
    "-o", $resolvedOutDir
)
if ($splashFound) {
    $vpkArgs += @("-s", $splashPath, "--splashProgressColor", $splashProgressColor)
}

function Join-CommandLine([string[]]$Pieces) {
    return ($Pieces | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }) -join ' '
}

Write-Host "=== Aura Launcher Release Packaging: $userFacingVersion (v$internalVersion) ===" -ForegroundColor Cyan
Write-Host "Mode            : $(if ($Execute) { 'EXECUTE' } else { 'PLAN ONLY (no commands run, no files changed)' })"
Write-Host "Repository      : $repoRoot"
Write-Host "Output directory: $resolvedOutDir"
Write-Host "Installer icon  : $iconPath"
Write-Host "Installer splash: $(if ($splashFound) { "$splashPath (progress bar $splashProgressColor)" } else { 'MISSING (see warning above)' })"
Write-Host "Temp workdir    : $taskTempRoot (removed on exit)"
Write-Host "Version files   : $(if ($UpdateVersionFiles) { 'will be updated (version.json, AuraLauncher.csproj)' } else { 'NOT modified' })"
Write-Host "Old-version cleanup: DISABLED (this script never deletes other versions or touches C:\AuraRelease)"
Write-Host ""
Write-Host "Planned publish command:" -ForegroundColor Yellow
Write-Host ("  dotnet " + (Join-CommandLine $publishArgs))
Write-Host "Planned vpk command:" -ForegroundColor Yellow
Write-Host ("  dotnet " + (Join-CommandLine $vpkArgs))

if (-not [string]::IsNullOrWhiteSpace($PreviousPackagesDir)) {
    if (-not (Test-Path $PreviousPackagesDir -PathType Container)) {
        throw "-PreviousPackagesDir not found: $PreviousPackagesDir"
    }
    $prevFiles = Get-ChildItem -Path $PreviousPackagesDir -File | Where-Object {
        ($_.Name -like "AuraLauncher-*.nupkg") -or ($_.Name -eq "releases.win.json") -or ($_.Name -eq "RELEASES")
    }
    Write-Host "Previous packages to copy into -OutDir (for delta generation):" -ForegroundColor Yellow
    if ($prevFiles) {
        $prevFiles | ForEach-Object { Write-Host "  $($_.Name)" }
    } else {
        Write-Host "  (none found in $PreviousPackagesDir)"
    }
} else {
    $prevFiles = @()
    Write-Host "Previous packages: none (-PreviousPackagesDir not set; vpk will produce a full package without deltas)"
}

if (-not $Execute) {
    Write-Host ""
    Write-Host "Plan validated OK. Re-run with -Execute to build and pack." -ForegroundColor Green
    return
}

# ---------------------------------------------------------------------------
# 5. Execution
# ---------------------------------------------------------------------------
if ($UpdateVersionFiles) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)

    $vObj = [ordered]@{
        internalVersion = $internalVersion
        userFacingVersion = $userFacingVersion
        changelog = $changelog
    }
    $jsonText = ($vObj | ConvertTo-Json -Depth 3) + "`n"
    [System.IO.File]::WriteAllText($versionJsonPath, $jsonText, $utf8NoBom)

    if (Test-Path $csprojPath) {
        $csprojText = [System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)
        $csprojText = $csprojText -replace '<Version>.*?</Version>', "<Version>$internalVersion</Version>"
        [System.IO.File]::WriteAllText($csprojPath, $csprojText, $utf8NoBom)
    }
}

try {
    [System.IO.Directory]::CreateDirectory($tempPublishDir) | Out-Null
    [System.IO.Directory]::CreateDirectory($resolvedOutDir) | Out-Null

    Write-Host "Step 1: Publishing win-x64 single-file release..." -ForegroundColor Yellow
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    foreach ($pf in $prevFiles) {
        Copy-Item -Path $pf.FullName -Destination (Join-Path $resolvedOutDir $pf.Name) -Force
    }

    Write-Host "Step 2: Packaging release with Velopack vpk CLI..." -ForegroundColor Yellow
    & dotnet @vpkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet vpk pack failed with exit code $LASTEXITCODE"
    }

    Write-Host "`nRelease packaging complete!" -ForegroundColor Green
    Write-Host "Artifacts in $($resolvedOutDir):"
    Get-ChildItem -Path $resolvedOutDir | Select-Object Name, @{Name = "Size (MB)"; Expression = { "{0:N2} MB" -f ($_.Length / 1MB) }} | Format-Table -AutoSize
}
finally {
    if (Test-Path $taskTempRoot) {
        try {
            Remove-Item -Path $taskTempRoot -Recurse -Force -ErrorAction SilentlyContinue
        } catch { }
    }
}
