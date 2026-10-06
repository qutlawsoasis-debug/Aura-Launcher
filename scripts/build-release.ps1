[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [string]$OutDir = "",

    [Parameter(Mandatory = $false)]
    [switch]$Increment
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "AuraLauncher.csproj"))) {
    $repoRoot = (Get-Location).Path
}

$versionJsonPath = Join-Path $repoRoot "version.json"
$internalVersion = "1.2.9"
$userFacingVersion = "beta 1.0.1"

if (Test-Path $versionJsonPath) {
    $vData = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
    $internalVersion = $vData.internalVersion
    $userFacingVersion = $vData.userFacingVersion
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

$Version = $internalVersion

# Save back to version.json
$vObj = [ordered]@{
    internalVersion = $internalVersion
    userFacingVersion = $userFacingVersion
}
$vObj | ConvertTo-Json -Depth 2 | Set-Content $versionJsonPath -Encoding UTF8

# Sync AuraLauncher.csproj
$csprojPath = Join-Path $repoRoot "AuraLauncher.csproj"
if (Test-Path $csprojPath) {
    (Get-Content $csprojPath) -replace '<Version>.*?</Version>', "<Version>$internalVersion</Version>" | Set-Content $csprojPath -Encoding UTF8
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = "C:\AuraRelease\$Version"
}

Write-Host "=== Aura Launcher Release Build: $userFacingVersion (v$Version) ===" -ForegroundColor Cyan
Write-Host "Target output directory: $OutDir"

$tempPublishDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "aura_pub_" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($tempPublishDir) | Out-Null

try {
    Write-Host "Step 1: Publishing win-x64 single-file release..." -ForegroundColor Yellow
    $csprojPath = Join-Path $repoRoot "AuraLauncher.csproj"
    
    $csprojPath = Join-Path $repoRoot "AuraLauncher.csproj"
    & dotnet publish $csprojPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$Version -o $tempPublishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $OutDir)) {
        [System.IO.Directory]::CreateDirectory($OutDir) | Out-Null
    }

    # If previous releases exist in parent C:\AuraRelease, copy previous package assets for delta creation
    $parentReleaseDir = "C:\AuraRelease"
    if (Test-Path $parentReleaseDir) {
        $prevReleases = Get-ChildItem -Path $parentReleaseDir -Directory | Where-Object { $_.FullName -ne $OutDir } | Sort-Object Name
        foreach ($prevDir in $prevReleases) {
            $prevFiles = Get-ChildItem -Path $prevDir.FullName -File | Where-Object { 
                $_.Name -like "*.nupkg" -or $_.Name -eq "releases.win.json" -or $_.Name -eq "assets.win.json"
            }
            foreach ($pf in $prevFiles) {
                $targetFile = Join-Path $OutDir $pf.Name
                if (-not (Test-Path $targetFile)) {
                    Copy-Item -Path $pf.FullName -Destination $targetFile -Force
                }
            }
        }
    }

    Write-Host "Step 2: Packaging release with Velopack vpk CLI..." -ForegroundColor Yellow
    $iconPath = Join-Path $repoRoot "app_icon.ico"
    $splashPath = Join-Path $repoRoot "splash.png"
    $vpkArgs = @(
        "pack",
        "-u", "AuraLauncher",
        "-v", $Version,
        "-p", $tempPublishDir,
        "-e", "AuraLauncher.exe",
        "--packTitle", "Aura",
        "-i", $iconPath,
        "-r", "win-x64",
        "-o", $OutDir
    )
    if (Test-Path $splashPath) {
        $vpkArgs += @("-s", $splashPath)
    }
    
    & dotnet vpk @vpkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet vpk pack failed with exit code $LASTEXITCODE"
    }

    Write-Host "`nRelease build complete!" -ForegroundColor Green
    Write-Host "Artifacts in $($OutDir):"
    Get-ChildItem -Path $OutDir | Select-Object Name, @{Name="Size (MB)"; Expression={ "{0:N2} MB" -f ($_.Length / 1MB) }} | Format-Table -AutoSize
}
finally {
    if (Test-Path $tempPublishDir) {
        try {
            Remove-Item -Path $tempPublishDir -Recurse -Force -ErrorAction SilentlyContinue
        } catch { }
    }
}
