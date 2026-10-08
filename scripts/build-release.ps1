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

$changelog = @()
if (Test-Path $versionJsonPath) {
    $vData = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
    $internalVersion = $vData.internalVersion
    $userFacingVersion = $vData.userFacingVersion
    if ($null -ne $vData.changelog) {
        $changelog = @($vData.changelog)
    }
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
    changelog = $changelog
}
$vObj | ConvertTo-Json -Depth 3 | Set-Content $versionJsonPath -Encoding UTF8

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

    # Храним в C:\AuraRelease максимум 5 последних версий (4 предыдущие + текущая)
    $parentReleaseDir = "C:\AuraRelease"
    $maxKeepReleases = 5
    if (Test-Path $parentReleaseDir) {
        $prevReleases = Get-ChildItem -Path $parentReleaseDir -Directory |
            Where-Object { $_.FullName -ne $OutDir -and ($_.Name -match '^\d+\.\d+\.\d+$') } |
            Sort-Object { [version]$_.Name } |
            Select-Object -Last ($maxKeepReleases - 1)

        $allowedPrevVersions = @($prevReleases | ForEach-Object { $_.Name })
        foreach ($prevDir in $prevReleases) {
            $prevFiles = Get-ChildItem -Path $prevDir.FullName -File | Where-Object {
                if ($_.Name -eq "releases.win.json" -or $_.Name -eq "assets.win.json" -or $_.Name -eq "RELEASES") {
                    return $true
                }
                if ($_.Name -like "AuraLauncher-*.nupkg") {
                    foreach ($av in $allowedPrevVersions) {
                        if ($_.Name -like "AuraLauncher-$av-*") { return $true }
                    }
                }
                return $false
            }
            foreach ($pf in $prevFiles) {
                $targetFile = Join-Path $OutDir $pf.Name
                Copy-Item -Path $pf.FullName -Destination $targetFile -Force
            }
        }
    }

    Write-Host "Step 2: Packaging release with Velopack vpk CLI..." -ForegroundColor Yellow
    $iconPath = Join-Path $repoRoot "Resources\aura-icon.ico"
    if (-not (Test-Path $iconPath)) {
        $iconPath = Join-Path $repoRoot "app_icon.ico"
    }
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

    # Автоочистка C:\AuraRelease: оставляем только 5 последних версий
    if (Test-Path $parentReleaseDir) {
        $allVerDirs = Get-ChildItem -Path $parentReleaseDir -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name }
        $keepDirs = @($allVerDirs | Select-Object -Last $maxKeepReleases)
        $keepNames = @($keepDirs | ForEach-Object { $_.Name })

        Get-ChildItem -Path $parentReleaseDir -Directory | Where-Object { $_.Name -notin $keepNames } | ForEach-Object {
            Write-Host "Cleaning old release folder: $($_.FullName)" -ForegroundColor DarkGray
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

        foreach ($kd in $keepDirs) {
            Get-ChildItem -Path $kd.FullName -Filter "AuraLauncher-*.nupkg" | ForEach-Object {
                $keepPkg = $false
                foreach ($kn in $keepNames) {
                    if ($_.Name -like "AuraLauncher-$kn-*") { $keepPkg = $true; break }
                }
                if (-not $keepPkg) {
                    Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        }
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
