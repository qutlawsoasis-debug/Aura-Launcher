[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = "C:\AuraRelease\$Version"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "AuraLauncher.csproj"))) {
    $repoRoot = (Get-Location).Path
}

Write-Host "=== Aura Launcher Release Build: v$Version ===" -ForegroundColor Cyan
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
    
    & dotnet vpk pack -u AuraLauncher -v $Version -p $tempPublishDir -e AuraLauncher.exe --packTitle "Aura" -i $iconPath -r win-x64 -o $OutDir
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
