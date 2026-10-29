# scripts/release-pack.ps1
# Automated, one-command release script for Aura-Pack (aura-glass -> Aura-Pack).
#
# Usage:
#   Plan / dry-run only (default, zero side-effects on git branches/PRs or target PackDir):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack"
#
#   Live release (creates branch release/aura-glass-<packVersion>, commits, pushes, creates PR):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -DryRun:$false
#     or
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -NoDryRun
#
# Requirements (per W4e & QA Return):
#   1. Parameters: -LauncherRef (default: origin/main), -PackDir (mandatory), -DryRun (default: ON).
#   2. Clean copy of sources via git worktree, gradlew build --no-daemon for both modules.
#   3. DryRun must NOT modify PackDir at all (staging in isolated temp copy, zero side-effects).
#   4. Strict boundaries: PackDir must not overlap Launcher, user profile or AppData.
#      Reparse points (symlinks/junctions) inside PackDir are strictly rejected.
#   5. Strict manifest validation: exactly 1 target entry with valid sha256, size, and packVersion.
#      Missing size/sha is a hard terminating error. Semantic diff verifies only 3 expected fields change.
#   6. Clean worktree/index required before live modifications; staged changes verified strictly to 4 files.
#   7. Run check-parity.ps1 in staging copy -> must PASS before any live modifications.
#   8. Clean up build worktree and staging directories in finally. %APPDATA% not touched.

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

# 1. Validate PackDir existence and resolve canonical path
if (-not (Test-Path -LiteralPath $PackDir)) {
    throw "PackDir does not exist: $PackDir"
}
$resolvedPackDir = (Resolve-Path -LiteralPath $PackDir).Path.TrimEnd('\')
$canonicalPackDirWithSlash = $resolvedPackDir + '\'

# 2. Locate Launcher git root
$launcherRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path.TrimEnd('\')
$canonicalLauncherWithSlash = $launcherRoot + '\'
if (-not (Test-Path (Join-Path $launcherRoot ".git"))) {
    throw "Launcher git root not found above scripts directory: $launcherRoot"
}

# 3. Security Boundary Checks
# PackDir must not overlap with Launcher repo
if ($resolvedPackDir -eq $launcherRoot -or
    $resolvedPackDir.StartsWith($canonicalLauncherWithSlash, [System.StringComparison]::OrdinalIgnoreCase) -or
    $launcherRoot.StartsWith($canonicalPackDirWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Security violation: PackDir cannot overlap with Launcher repository root ($launcherRoot)."
}

# Reject if PackDir is user profile root or AppData
$forbiddenRoots = @(
    $env:USERPROFILE,
    $env:APPDATA,
    $env:LOCALAPPDATA
)
foreach ($fp in $forbiddenRoots) {
    if ([string]::IsNullOrWhiteSpace($fp)) { continue }
    $resolvedFp = (Resolve-Path -LiteralPath $fp -ErrorAction SilentlyContinue)
    if ($resolvedFp) {
        $canonicalFp = $resolvedFp.Path.TrimEnd('\')
        if ($resolvedPackDir -eq $canonicalFp) {
            throw "Security violation: PackDir cannot be root of user profile or AppData ($canonicalFp)."
        }
        # Explicit check for %APPDATA%\.aura
        $auraAppDir = (Join-Path $canonicalFp ".aura")
        if ($resolvedPackDir.StartsWith($auraAppDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Security violation: PackDir cannot be within %APPDATA%\.aura."
        }
    }
}

# Check git repository root and remote origin
if (Get-Command git -ErrorAction SilentlyContinue) {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $gitTopLevel = & git -C $resolvedPackDir rev-parse --show-toplevel 2>$null
        $topExit = $LASTEXITCODE
        if ($topExit -eq 0 -and [string]::IsNullOrWhiteSpace($gitTopLevel) -eq $false) {
            $resolvedGitTopLevel = (Resolve-Path -LiteralPath $gitTopLevel.Trim()).Path.TrimEnd('\')
            if ($resolvedGitTopLevel -ne $resolvedPackDir) {
                throw "PackDir '$resolvedPackDir' is not the root of its git repository (root is '$resolvedGitTopLevel')."
            }
        }
    } catch {
        if ($_.Exception.Message -match "is not the root") { throw }
    } finally {
        $ErrorActionPreference = $prevEap
    }

    $ErrorActionPreference = 'Continue'
    try {
        $gitRemoteOrigin = & git -C $resolvedPackDir remote get-url origin 2>$null
        $remoteExit = $LASTEXITCODE
        if ($remoteExit -eq 0 -and [string]::IsNullOrWhiteSpace($gitRemoteOrigin) -eq $false) {
            if ($gitRemoteOrigin -notmatch "Aura-Pack") {
                throw "PackDir git remote '$gitRemoteOrigin' does not point to an Aura-Pack repository."
            }
        }
    } catch {
        if ($_.Exception.Message -match "does not point to an Aura-Pack repository") { throw }
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

# 4. ReparsePoint and Path Validation
function Assert-SafePathInsidePackDir([string]$fullPath, [string]$packRoot) {
    $canonicalPackRoot = (Resolve-Path -LiteralPath $packRoot).Path.TrimEnd('\')
    $canonicalPackWithSlash = $canonicalPackRoot + '\'

    $current = $fullPath
    while ($current -and $current.Length -ge $canonicalPackRoot.Length) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "Security violation: Path '$current' is a reparse point (junction/symlink) inside PackDir."
            }
            $resolvedCurrent = (Resolve-Path -LiteralPath $current).Path.TrimEnd('\')
            if ($resolvedCurrent -ne $canonicalPackRoot -and -not $resolvedCurrent.StartsWith($canonicalPackWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Security violation: Path '$current' resolves outside PackDir ('$resolvedCurrent')."
            }
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current -or [string]::IsNullOrWhiteSpace($parent)) { break }
        $current = $parent
    }
}

function Assert-NoReparsePointsInPack([string]$packRoot) {
    $rootItem = Get-Item -LiteralPath $packRoot -Force
    if ($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Security violation: Root directory '$packRoot' is a reparse point (junction/symlink)."
    }

    $items = Get-ChildItem -LiteralPath $packRoot -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($item in $items) {
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw "Security violation: Path '$($item.FullName)' is a reparse point (junction/symlink). Reparse points are forbidden inside PackDir."
        }
        $resolved = (Resolve-Path -LiteralPath $item.FullName).Path.TrimEnd('\')
        if ($resolved -ne $packRoot -and -not $resolved.StartsWith($canonicalPackDirWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Security violation: Path '$($item.FullName)' resolves outside PackDir ('$resolved')."
        }
    }
}

Assert-NoReparsePointsInPack -packRoot $resolvedPackDir

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

Assert-SafePathInsidePackDir $manifest120 $resolvedPackDir
Assert-SafePathInsidePackDir $manifest121 $resolvedPackDir
Assert-SafePathInsidePackDir $parityScript $resolvedPackDir

# 5. Check worktree/index cleanliness if running in live mode
if (-not $isDryRun -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $gitStatus = & git -C $resolvedPackDir status --porcelain 2>$null
        $statusExit = $LASTEXITCODE
        if ($statusExit -eq 0 -and $gitStatus) {
            $dirtyLines = @($gitStatus -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
            if ($dirtyLines.Count -gt 0) {
                throw "PackDir git index/worktree is not clean. Staged or uncommitted changes detected:`n$($dirtyLines -join "`n")"
            }
        }
    } catch {
        if ($_.Exception.Message -match "PackDir git index/worktree is not clean") { throw }
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

# 6. Verify required CLI tools
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

# Helper to validate and prepare manifest update (fail-closed, strictly preserves formatting)
function Process-And-ValidateManifest(
    [string]$manifestFilePath,
    [string]$newVersion,
    [string]$newSha,
    [long]$newSize
) {
    if (-not (Test-Path -LiteralPath $manifestFilePath)) {
        throw "Manifest file not found: $manifestFilePath"
    }

    $raw = [System.IO.File]::ReadAllText($manifestFilePath, [System.Text.Encoding]::UTF8)

    # 1. Parse JSON to validate syntax and structure
    $json = $null
    try {
        $json = $raw | ConvertFrom-Json
    } catch {
        throw "Manifest '$manifestFilePath' is not valid JSON: $_"
    }

    # 2. Validate root packVersion
    $pvProp = $json.PSObject.Properties['packVersion']
    if ($pvProp -eq $null -or [string]::IsNullOrWhiteSpace("$($pvProp.Value)")) {
        throw "Manifest '$manifestFilePath' is missing required root 'packVersion' property."
    }

    # 3. Validate files collection
    $filesProp = $json.PSObject.Properties['files']
    if ($filesProp -eq $null -or -not ($filesProp.Value -is [System.Collections.IEnumerable])) {
        throw "Manifest '$manifestFilePath' is missing 'files' array."
    }

    # 4. Find matching target entry in parsed JSON
    $matchingEntries = @($filesProp.Value | Where-Object {
        $p = $_.PSObject.Properties['path']
        $p -and (($p.Value -replace '\\', '/') -eq 'mods/aura-glass-1.0.0.jar')
    })

    if ($matchingEntries.Count -eq 0) {
        throw "Manifest '$manifestFilePath' does not contain target entry 'mods/aura-glass-1.0.0.jar'."
    }
    if ($matchingEntries.Count -gt 1) {
        throw "Manifest '$manifestFilePath' contains $($matchingEntries.Count) entries for 'mods/aura-glass-1.0.0.jar'; expected exactly 1."
    }

    $targetEntry = $matchingEntries[0]

    # Validate sha256 property exists and is non-empty
    $shaProp = $targetEntry.PSObject.Properties['sha256']
    if ($shaProp -eq $null -or [string]::IsNullOrWhiteSpace("$($shaProp.Value)")) {
        throw "Manifest '$manifestFilePath' target entry 'mods/aura-glass-1.0.0.jar' is missing required 'sha256' property."
    }

    # Validate size property exists and is not null
    $sizeProp = $targetEntry.PSObject.Properties['size']
    if ($sizeProp -eq $null -or $sizeProp.Value -eq $null -or [string]::IsNullOrWhiteSpace("$($sizeProp.Value)")) {
        throw "Manifest '$manifestFilePath' target entry 'mods/aura-glass-1.0.0.jar' is missing required 'size' property."
    }

    # 5. Regex updates preserving original indentation and formatting
    $pvRegex = New-Object System.Text.RegularExpressions.Regex '("packVersion"\s*:\s*")[^"]+(")'
    $pvMatches = $pvRegex.Matches($raw)
    if ($pvMatches.Count -ne 1) {
        throw "Manifest '$manifestFilePath' contains $($pvMatches.Count) matches for 'packVersion' property; expected exactly 1."
    }
    $updated = $pvRegex.Replace($raw, ('${1}' + $newVersion + '$2'), 1)

    $blockRegex = New-Object System.Text.RegularExpressions.Regex '(\{[^{}]*?"path"\s*:\s*"mods[/\\]aura-glass-1\.0\.0\.jar"[^{}]*?\})', ([System.Text.RegularExpressions.RegexOptions]::Singleline)
    $blockMatches = $blockRegex.Matches($updated)
    if ($blockMatches.Count -ne 1) {
        throw "Manifest '$manifestFilePath' contains $($blockMatches.Count) matching JSON blocks for 'mods/aura-glass-1.0.0.jar'; expected exactly 1."
    }

    $blockMatch = $blockMatches[0]
    $blockText = $blockMatch.Groups[1].Value

    # Verify that the block has exactly 1 sha256 property and exactly 1 size property
    $shaRegex = New-Object System.Text.RegularExpressions.Regex '("sha256"\s*:\s*")[0-9a-fA-F]+(")'
    $shaBlockMatches = $shaRegex.Matches($blockText)
    if ($shaBlockMatches.Count -ne 1) {
        throw "Manifest '$manifestFilePath' target entry block contains $($shaBlockMatches.Count) matches for 'sha256'; expected exactly 1."
    }

    $sizeRegex = New-Object System.Text.RegularExpressions.Regex '("size"\s*:\s*)\d+'
    $sizeBlockMatches = $sizeRegex.Matches($blockText)
    if ($sizeBlockMatches.Count -ne 1) {
        throw "Manifest '$manifestFilePath' target entry block contains $($sizeBlockMatches.Count) matches for 'size'; expected exactly 1."
    }

    $replacedBlock = $shaRegex.Replace($blockText, ('${1}' + $newSha + '$2'), 1)
    $replacedBlock = $sizeRegex.Replace($replacedBlock, ('${1}' + $newSize), 1)

    $updated = $updated.Substring(0, $blockMatch.Index) + $replacedBlock + $updated.Substring($blockMatch.Index + $blockMatch.Length)

    # 6. Post-update JSON validation & semantic diff
    $newJson = $null
    try {
        $newJson = $updated | ConvertFrom-Json
    } catch {
        throw "Updated manifest text for '$manifestFilePath' failed JSON parse validation: $_"
    }

    if ($newJson.packVersion -ne $newVersion) {
        throw "Post-update validation failed: packVersion was '$($newJson.packVersion)', expected '$newVersion'."
    }

    $newMatchingEntries = @($newJson.files | Where-Object {
        $p = $_.PSObject.Properties['path']
        $p -and (($p.Value -replace '\\', '/') -eq 'mods/aura-glass-1.0.0.jar')
    })
    if ($newMatchingEntries.Count -ne 1) {
        throw "Post-update validation failed: expected 1 target entry, found $($newMatchingEntries.Count)."
    }

    $newTarget = $newMatchingEntries[0]
    if ($newTarget.sha256 -ne $newSha) {
        throw "Post-update validation failed: sha256 was '$($newTarget.sha256)', expected '$newSha'."
    }
    if ([long]$newTarget.size -ne [long]$newSize) {
        throw "Post-update validation failed: size was '$($newTarget.size)', expected '$newSize'."
    }

    # Semantic comparison: root properties count must be unchanged
    if (@($json.PSObject.Properties).Count -ne @($newJson.PSObject.Properties).Count) {
        throw "Post-update semantic diff failed: root property count changed."
    }
    foreach ($p in $json.PSObject.Properties) {
        if ($p.Name -eq 'packVersion' -or $p.Name -eq 'files') { continue }
        $newVal = $newJson.PSObject.Properties[$p.Name]
        if ($newVal -eq $null -or "$($p.Value)" -ne "$($newVal.Value)") {
            throw "Post-update semantic diff failed: unexpected change in root property '$($p.Name)'."
        }
    }

    # Check files array length
    if (@($json.files).Count -ne @($newJson.files).Count) {
        throw "Post-update semantic diff failed: files count changed."
    }

    # Check that only the target entry changed
    $oldFilesList = @($json.files)
    $newFilesList = @($newJson.files)
    for ($i = 0; $i -lt $oldFilesList.Count; $i++) {
        $oldF = $oldFilesList[$i]
        $newF = $newFilesList[$i]
        $isTarget = (($oldF.path -replace '\\', '/') -eq 'mods/aura-glass-1.0.0.jar')
        if (-not $isTarget) {
            if ($oldF.path -ne $newF.path -or $oldF.sha256 -ne $newF.sha256 -or $oldF.size -ne $newF.size) {
                throw "Post-update semantic diff failed: unexpected modification in non-target file entry '$($oldF.path)'."
            }
        }
    }

    return $updated
}

# Create clean worktree for the requested LauncherRef
$worktreeGuid = [Guid]::NewGuid().ToString("N")
$worktreeDir = Join-Path $env:TEMP "aura-glass-build-$worktreeGuid"
$stagingGuid = [Guid]::NewGuid().ToString("N")
$stagingDir = Join-Path $env:TEMP "aura-pack-stage-$stagingGuid"

Write-Host "Creating clean worktree from $LauncherRef at $worktreeDir..." -ForegroundColor Cyan
git -c core.longpaths=true -C $launcherRoot worktree add --detach $worktreeDir $LauncherRef
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create git worktree for ref '$LauncherRef'"
}

try {
    # 7. Build both modules in worktree
    Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass")
    Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass-1.21")

    # Locate freshly built jars
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

    # 8. Compute common UTC packVersion
    $packVersion = (Get-Date).ToUniversalTime().ToString("yyyyMMdd.HHmm")

    Write-Host "`nTarget Pack Version: $packVersion" -ForegroundColor Green
    Write-Host "aura-glass (1.20.1):  sha256=$sha120, size=$size120" -ForegroundColor Green
    Write-Host "aura-glass (1.21.1):  sha256=$sha121, size=$size121" -ForegroundColor Green

    # 9. Validate and prepare updated manifests in memory (atomic, fail-closed)
    $updatedManifestContent120 = Process-And-ValidateManifest -manifestFilePath $manifest120 -newVersion $packVersion -newSha $sha120 -newSize $size120
    $updatedManifestContent121 = Process-And-ValidateManifest -manifestFilePath $manifest121 -newVersion $packVersion -newSha $sha121 -newSize $size121

    # 10. Prepare isolated staging copy in temp (PackDir is untouched)
    Write-Host "Creating staging copy in $stagingDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    # Copy files from PackDir into stagingDir (excluding .git)
    Get-ChildItem -LiteralPath $resolvedPackDir -Force | Where-Object { $_.Name -ne '.git' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stagingDir $_.Name) -Recurse -Force
    }

    # Place new jars and manifests into staging copy
    $stagingJar120 = Join-Path $stagingDir "mods\aura-glass-1.0.0.jar"
    $stagingJar121 = Join-Path $stagingDir "versions\1.21.1\mods\aura-glass-1.0.0.jar"
    $null = New-Item -ItemType Directory -Path (Split-Path $stagingJar120 -Parent) -Force -ErrorAction SilentlyContinue
    $null = New-Item -ItemType Directory -Path (Split-Path $stagingJar121 -Parent) -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $jar120 -Destination $stagingJar120 -Force
    Copy-Item -LiteralPath $jar121 -Destination $stagingJar121 -Force

    $stagingManifest120 = Join-Path $stagingDir "manifest.json"
    $stagingManifest121 = Join-Path $stagingDir "manifest-1.21.1.json"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($stagingManifest120, $updatedManifestContent120, $utf8NoBom)
    [System.IO.File]::WriteAllText($stagingManifest121, $updatedManifestContent121, $utf8NoBom)

    # 11. Run parity check in isolated staging environment
    $stagingParityScript = Join-Path $stagingDir "tools\check-parity.ps1"
    $stagingExceptions = Join-Path $stagingDir "tools\parity-exceptions.json"
    Write-Host "Running check-parity.ps1 in staging environment..." -ForegroundColor Cyan
    & $stagingParityScript -Root120 $stagingDir -Root121 (Join-Path $stagingDir "versions\1.21.1") -ExceptionsFile $stagingExceptions
    if ($LASTEXITCODE -ne 0) {
        throw "PackDir parity check failed in staging with exit code $LASTEXITCODE"
    }

    # 12. Handle DryRun vs Live Release
    if ($isDryRun) {
        Write-Host "`n==================================================" -ForegroundColor Green
        Write-Host "                 DRY-RUN PLAN & DIFF              " -ForegroundColor Green
        Write-Host "==================================================" -ForegroundColor Green
        Write-Host "LauncherRef: $LauncherRef"
        Write-Host "PackDir:     $resolvedPackDir"
        Write-Host "packVersion: $packVersion"
        Write-Host "1.20.1 Jar:  mods/aura-glass-1.0.0.jar ($size120 bytes, sha256: $sha120)"
        Write-Host "1.21.1 Jar:  versions/1.21.1/mods/aura-glass-1.0.0.jar ($size121 bytes, sha256: $sha121)"
        Write-Host "`n--- Git Diff of Manifests (DryRun) ---" -ForegroundColor Yellow
        if (Get-Command git -ErrorAction SilentlyContinue) {
            & git -c core.longpaths=true diff --no-index -- $manifest120 $stagingManifest120
            & git -c core.longpaths=true diff --no-index -- $manifest121 $stagingManifest121
            $global:LASTEXITCODE = 0
        }
        Write-Host "`n[DRY-RUN COMPLETE] Mode is DryRun. Original PackDir was NOT modified. No branch created, no commits made, no push, no PR created." -ForegroundColor Green
    } else {
        Write-Host "`n==================================================" -ForegroundColor Magenta
        Write-Host "               LIVE RELEASE EXECUTION             " -ForegroundColor Magenta
        Write-Host "==================================================" -ForegroundColor Magenta

        # Fail-closed check: Ensure PackDir git worktree and index are completely clean
        if (Get-Command git -ErrorAction SilentlyContinue) {
            $gitStatus = git -C $resolvedPackDir status --porcelain 2>$null
            if ($LASTEXITCODE -eq 0 -and $gitStatus) {
                $dirtyLines = @($gitStatus -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
                if ($dirtyLines.Count -gt 0) {
                    throw "PackDir git index/worktree is not clean. Refusing to release with existing modifications:`n$($dirtyLines -join "`n")"
                }
            }
        }

        $branchName = "release/aura-glass-$packVersion"
        Write-Host "Creating branch $branchName in $resolvedPackDir..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir checkout -b $branchName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create branch $branchName in $resolvedPackDir"
        }

        # Validate destination paths and copy jars to PackDir
        $destJar120 = Join-Path $resolvedPackDir "mods\aura-glass-1.0.0.jar"
        $destJar121 = Join-Path $resolvedPackDir "versions\1.21.1\mods\aura-glass-1.0.0.jar"
        Assert-SafePathInsidePackDir $destJar120 $resolvedPackDir
        Assert-SafePathInsidePackDir $destJar121 $resolvedPackDir

        $null = New-Item -ItemType Directory -Path (Split-Path $destJar120 -Parent) -Force -ErrorAction SilentlyContinue
        $null = New-Item -ItemType Directory -Path (Split-Path $destJar121 -Parent) -Force -ErrorAction SilentlyContinue

        Copy-Item -LiteralPath $jar120 -Destination $destJar120 -Force
        Copy-Item -LiteralPath $jar121 -Destination $destJar121 -Force

        # Write updated manifests to PackDir
        Assert-SafePathInsidePackDir $manifest120 $resolvedPackDir
        Assert-SafePathInsidePackDir $manifest121 $resolvedPackDir
        [System.IO.File]::WriteAllText($manifest120, $updatedManifestContent120, $utf8NoBom)
        [System.IO.File]::WriteAllText($manifest121, $updatedManifestContent121, $utf8NoBom)

        Write-Host "Staging release artifacts..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir add mods/aura-glass-1.0.0.jar versions/1.21.1/mods/aura-glass-1.0.0.jar manifest.json manifest-1.21.1.json
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stage release artifacts in $resolvedPackDir"
        }

        # Verify staged files strictly match expected set
        $stagedFiles = @(git -c core.longpaths=true -C $resolvedPackDir diff --cached --name-only)
        $stagedNormalized = @($stagedFiles | ForEach-Object { ($_ -replace '\\', '/').Trim() } | Where-Object { $_.Length -gt 0 } | Sort-Object)
        $expectedFiles = @('manifest-1.21.1.json', 'manifest.json', 'mods/aura-glass-1.0.0.jar', 'versions/1.21.1/mods/aura-glass-1.0.0.jar') | Sort-Object

        if ($stagedNormalized.Count -ne 4 -or ($stagedNormalized -join ';') -ne ($expectedFiles -join ';')) {
            throw "Security violation: Staged index contains unexpected files! Expected strictly [$($expectedFiles -join ', ')], but found: [$($stagedNormalized -join ', ')]"
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
    Write-Host "Cleaning up build worktree and staging directories..." -ForegroundColor Cyan
    if ($stagingDir -and (Test-Path -LiteralPath $stagingDir)) {
        cmd /c "rd /s /q `"$stagingDir`"" 2>$null
        if (Test-Path -LiteralPath $stagingDir) {
            Remove-Item -LiteralPath "\\?\$stagingDir" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if ($worktreeDir -and (Test-Path -LiteralPath $worktreeDir)) {
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
