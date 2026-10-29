# scripts/release-pack.ps1
# Automated, one-command release script for Aura-Pack (aura-glass -> Aura-Pack).
#
# Usage:
#   Plan / dry-run only (default, zero side-effects on git branches/PRs):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack"
#
#   Live release (creates branch release/aura-glass-<packVersion>, commits, pushes, creates PR):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -DryRun:$false
#     or
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -NoDryRun
#
# Requirements (per W4e):
#   1. Parameters: -LauncherRef (default: origin/main), -PackDir (mandatory), -DryRun (default: ON).
#   2. Clean copy of sources via git worktree, gradlew build --no-daemon for both modules.
#   3. Copy jar to PackDir/mods/ and PackDir/versions/1.21.1/mods/.
#   4. Update ONLY mods/aura-glass-1.0.0.jar (sha256, size) and packVersion (UTC yyyyMMdd.HHmm) in manifests.
#   5. Run PackDir/tools/check-parity.ps1 -> must PASS.
#   6. Without -DryRun: branch release/aura-glass-<packVersion>, commit, push, gh pr create (no merge).
#      With -DryRun: print plan and diff.
#   7. Delete temp worktree; no clean/reset/stash in other folders; %APPDATA% not touched.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$LauncherRef = 'origin/main',

    [Parameter(Mandatory = $true)]
    [string]$PackDir,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun = $true,

    [Parameter(Mandatory = $false)]
    [switch]$NoDryRun
)

$ErrorActionPreference = 'Stop'

# Determine dry-run status
$isDryRun = [bool]$DryRun
if ($NoDryRun.IsPresent) {
    $isDryRun = $false
}

# 1. Validate PackDir
if (-not (Test-Path -LiteralPath $PackDir)) {
    throw "PackDir does not exist: $PackDir"
}
$resolvedPackDir = (Resolve-Path -LiteralPath $PackDir).Path

$manifest120 = Join-Path $resolvedPackDir "manifest.json"
$manifest121 = Join-Path $resolvedPackDir "manifest-1.21.1.json"
$parityScript = Join-Path $resolvedPackDir "tools\check-parity.ps1"

if (-not (Test-Path -LiteralPath $manifest120)) {
    throw "Missing manifest.json in PackDir: $resolvedPackDir"
}
if (-not (Test-Path -LiteralPath $manifest121)) {
    throw "Missing manifest-1.21.1.json in PackDir: $resolvedPackDir"
}
if (-not (Test-Path -LiteralPath $parityScript)) {
    throw "Missing tools\check-parity.ps1 in PackDir: $resolvedPackDir"
}

# 2. Locate Launcher repo root
$launcherRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not (Test-Path (Join-Path $launcherRoot ".git"))) {
    throw "Launcher git root not found above scripts directory: $launcherRoot"
}

# 3. Verify prerequisites
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git command is required but not found in PATH."
}
if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
    throw "java command is required to run GradleWrapperMain."
}

# Helper to run Gradle build with --no-daemon
function Invoke-GradleBuild([string]$modulePath) {
    Write-Host "Building $modulePath with Gradle (--no-daemon)..." -ForegroundColor Yellow
    Push-Location $modulePath
    try {
        if (Test-Path ".\gradlew.bat") {
            & ".\gradlew.bat" build --no-daemon
        } elseif (Test-Path ".\gradle\wrapper\gradle-wrapper.jar") {
            & java -classpath gradle/wrapper/gradle-wrapper.jar org.gradle.wrapper.GradleWrapperMain build --no-daemon
        } elseif (Get-Command "gradle" -ErrorAction SilentlyContinue) {
            & gradle build --no-daemon
        } else {
            throw "Neither gradlew.bat, gradle-wrapper.jar, nor gradle command found in $modulePath"
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Gradle build failed with exit code $LASTEXITCODE in $modulePath"
        }
    } finally {
        Pop-Location
    }
}

# Helper to update manifest without re-generating JSON (preserves exact indentation and formatting)
function Update-PackManifest([string]$manifestFilePath, [string]$newVersion, [string]$newSha, [long]$newSize) {
    $raw = [System.IO.File]::ReadAllText($manifestFilePath, [System.Text.Encoding]::UTF8)

    # Replace packVersion
    $updated = [System.Text.RegularExpressions.Regex]::Replace(
        $raw,
        '("packVersion"\s*:\s*")[^"]+(")',
        ('${1}' + $newVersion + '$2'),
        [System.Text.RegularExpressions.RegexOptions]::None
    )

    # Replace sha256 and size within the mods/aura-glass-1.0.0.jar block
    $blockPattern = '(\{[^{}]*?"path"\s*:\s*"mods[/\\]aura-glass-1\.0\.0\.jar"[^{}]*?\})'
    $match = [System.Text.RegularExpressions.Regex]::Match($updated, $blockPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        throw "Could not find 'mods/aura-glass-1.0.0.jar' block in $manifestFilePath"
    }

    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($m)
        $b = $m.Groups[1].Value
        $b = [System.Text.RegularExpressions.Regex]::Replace($b, '("sha256"\s*:\s*")[0-9a-fA-F]+(")', ('${1}' + $newSha + '$2'))
        $b = [System.Text.RegularExpressions.Regex]::Replace($b, '("size"\s*:\s*)\d+', ('${1}' + $newSize))
        return $b
    }

    $updated = [System.Text.RegularExpressions.Regex]::Replace(
        $updated,
        $blockPattern,
        $evaluator,
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($manifestFilePath, $updated, $utf8NoBom)
}

# Create clean worktree for the requested LauncherRef
$worktreeGuid = [Guid]::NewGuid().ToString("N")
$worktreeDir = Join-Path $env:TEMP "aura-glass-build-$worktreeGuid"

Write-Host "Creating clean worktree from $LauncherRef at $worktreeDir..." -ForegroundColor Cyan
git -c core.longpaths=true -C $launcherRoot worktree add --detach $worktreeDir $LauncherRef
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create git worktree for ref '$LauncherRef'"
}

try {
    # 4. Build both modules
    Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass")
    Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass-1.21")

    # Locate jars
    $jar120 = Join-Path $worktreeDir "aura-glass\build\libs\aura-glass-1.0.0.jar"
    $jar121 = Join-Path $worktreeDir "aura-glass-1.21\build\libs\aura-glass-1.0.0.jar"

    if (-not (Test-Path -LiteralPath $jar120)) {
        throw "Expected jar not found: $jar120"
    }
    if (-not (Test-Path -LiteralPath $jar121)) {
        throw "Expected jar not found: $jar121"
    }

    $sha120 = (Get-FileHash -Path $jar120 -Algorithm SHA256).Hash.ToLowerInvariant()
    $size120 = (Get-Item -LiteralPath $jar120).Length

    $sha121 = (Get-FileHash -Path $jar121 -Algorithm SHA256).Hash.ToLowerInvariant()
    $size121 = (Get-Item -LiteralPath $jar121).Length

    # 5. Compute common UTC packVersion
    $packVersion = (Get-Date).ToUniversalTime().ToString("yyyyMMdd.HHmm")

    Write-Host "`nTarget Pack Version: $packVersion" -ForegroundColor Green
    Write-Host "aura-glass (1.20.1):  sha256=$sha120, size=$size120" -ForegroundColor Green
    Write-Host "aura-glass (1.21.1):  sha256=$sha121, size=$size121" -ForegroundColor Green

    # 6. Copy jars to PackDir
    $destJar120 = Join-Path $resolvedPackDir "mods\aura-glass-1.0.0.jar"
    $destJar121 = Join-Path $resolvedPackDir "versions\1.21.1\mods\aura-glass-1.0.0.jar"

    $null = New-Item -ItemType Directory -Path (Split-Path $destJar120 -Parent) -Force -ErrorAction SilentlyContinue
    $null = New-Item -ItemType Directory -Path (Split-Path $destJar121 -Parent) -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $jar120 -Destination $destJar120 -Force
    Copy-Item -LiteralPath $jar121 -Destination $destJar121 -Force
    Write-Host "Copied jars to PackDir targets." -ForegroundColor Cyan

    # 7. Update manifests
    Update-PackManifest -manifestFilePath $manifest120 -newVersion $packVersion -newSha $sha120 -newSize $size120
    Update-PackManifest -manifestFilePath $manifest121 -newVersion $packVersion -newSha $sha121 -newSize $size121
    Write-Host "Updated manifest.json and manifest-1.21.1.json." -ForegroundColor Cyan

    # 8. Run check-parity.ps1
    Write-Host "Running check-parity.ps1 in PackDir..." -ForegroundColor Cyan
    & $parityScript -Root120 $resolvedPackDir -Root121 (Join-Path $resolvedPackDir "versions\1.21.1") -ExceptionsFile (Join-Path $resolvedPackDir "tools\parity-exceptions.json")
    if ($LASTEXITCODE -ne 0) {
        throw "PackDir parity check failed with exit code $LASTEXITCODE"
    }

    # 9. Handle DryRun vs Live Release
    if ($isDryRun) {
        Write-Host "`n==================================================" -ForegroundColor Green
        Write-Host "                 DRY-RUN PLAN & DIFF              " -ForegroundColor Green
        Write-Host "==================================================" -ForegroundColor Green
        Write-Host "LauncherRef: $LauncherRef"
        Write-Host "PackDir:     $resolvedPackDir"
        Write-Host "packVersion: $packVersion"
        Write-Host "1.20.1 Jar:  mods/aura-glass-1.0.0.jar ($size120 bytes, sha256: $sha120)"
        Write-Host "1.21.1 Jar:  versions/1.21.1/mods/aura-glass-1.0.0.jar ($size121 bytes, sha256: $sha121)"
        Write-Host "`n--- Git Diff of Manifests ---" -ForegroundColor Yellow
        git -c core.longpaths=true -C $resolvedPackDir diff manifest.json manifest-1.21.1.json
        Write-Host "`n[DRY-RUN COMPLETE] Mode is DryRun. No branch created, no commits made, no push, no PR created." -ForegroundColor Green
    } else {
        Write-Host "`n==================================================" -ForegroundColor Magenta
        Write-Host "               LIVE RELEASE EXECUTION             " -ForegroundColor Magenta
        Write-Host "==================================================" -ForegroundColor Magenta

        $branchName = "release/aura-glass-$packVersion"
        Write-Host "Creating branch $branchName in $resolvedPackDir..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir checkout -b $branchName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create branch $branchName in $resolvedPackDir"
        }

        Write-Host "Staging release artifacts..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir add mods/aura-glass-1.0.0.jar versions/1.21.1/mods/aura-glass-1.0.0.jar manifest.json manifest-1.21.1.json
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stage release artifacts in $resolvedPackDir"
        }

        $commitMsg = "release: update aura-glass to $packVersion"
        Write-Host "Committing changes: $commitMsg" -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir commit -m $commitMsg
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to commit in $resolvedPackDir"
        }

        Write-Host "Pushing branch $branchName..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir push -u origin $branchName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push $branchName"
        }

        Write-Host "Creating Pull Request (without merge)..." -ForegroundColor Cyan
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            $prBody = @"
## Summary
Automated release of aura-glass $packVersion compiled from $LauncherRef.

### Artifacts:
- **1.20.1**: ``mods/aura-glass-1.0.0.jar``
  - SHA256: ``$sha120``
  - Size: $size120 bytes
- **1.21.1**: ``versions/1.21.1/mods/aura-glass-1.0.0.jar``
  - SHA256: ``$sha121``
  - Size: $size121 bytes

### Verification:
- Parity check (check-parity.ps1): PASS
"@
            gh pr create --repo qutlawsoasis-debug/Aura-Pack --title $commitMsg --body $prBody --head $branchName
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "gh pr create failed with exit code $LASTEXITCODE. Branch has been pushed to origin/$branchName."
            }
        } else {
            Write-Warning "gh CLI not found; branch $branchName pushed, please create PR manually."
        }
        Write-Host "`n[SUCCESS] Pack release completed successfully." -ForegroundColor Green
    }
} finally {
    Write-Host "Cleaning up worktree $worktreeDir..." -ForegroundColor Cyan
    if (Test-Path -LiteralPath $worktreeDir) {
        git -c core.longpaths=true -C $launcherRoot worktree remove --force $worktreeDir 2>$null
        if (Test-Path -LiteralPath $worktreeDir) {
            cmd /c "rd /s /q `"$worktreeDir`"" 2>$null
            if (Test-Path -LiteralPath $worktreeDir) {
                Remove-Item -LiteralPath "\\?\$worktreeDir" -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        git -c core.longpaths=true -C $launcherRoot worktree prune 2>$null
    }
}
