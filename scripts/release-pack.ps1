# scripts/release-pack.ps1
# Automated, one-command release script for Aura-Pack.
# Supports releasing aura-client (-ClientJar120, -ClientJar121) and/or aura-glass (-BuildAuraGlass).
#
# Usage:
#   Plan / dry-run only (default, zero side-effects on git branches/PRs or target PackDir):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -ClientJar120 "path\aura-client-1.0.0.jar" -ClientJar121 "path\aura-client-1.0.0.jar"
#
#   Live release (creates branch release/aura-client-<packVersion>, commits, pushes, creates PR):
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -ClientJar120 "path\aura-client-1.0.0.jar" -ClientJar121 "path\aura-client-1.0.0.jar" -NoDryRun
#
#   Optional aura-glass release:
#     powershell -ExecutionPolicy Bypass -File scripts\release-pack.ps1 -PackDir "..\Aura-Pack" -BuildAuraGlass
#
# Requirements:
#   1. Parameters: -LauncherRef (default: origin/main), -PackDir (mandatory), -ClientJar120, -ClientJar121, -BuildAuraGlass, -DryRun (default: ON), -NoDryRun.
#   2. Building aura-glass is optional via -BuildAuraGlass switch. When enabled, builds in clean git worktree via gradlew build --no-daemon.
#   3. Client jars (-ClientJar120, -ClientJar121) placed into mods/ and versions/1.21.1/mods/.
#   4. DryRun must NOT modify PackDir at all (staging in isolated temp copy, zero side-effects).
#   5. Strict boundaries: PackDir must not overlap Launcher, user profile or AppData.
#      Reparse points (symlinks/junctions) inside PackDir and inputs are strictly rejected.
#   6. Fail-closed git repository check: valid Git root, remote origin matching qutlawsoasis-debug/Aura-Pack, clean status.
#   7. Manifest validation: adds or updates target entry (path, sha256, size, mode sync) and packVersion, preserving formatting.
#   8. Parity check (check-parity.ps1) run in staging copy -> must PASS before any live modifications.
#   9. Staged changes verified strictly against expected file set.
#  10. Clean up worktree and staging directories in finally. %APPDATA% not touched.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$LauncherRef = 'origin/main',

    [Parameter(Mandatory = $true)]
    [string]$PackDir,

    [Parameter(Mandatory = $false)]
    [string]$ClientJar120,

    [Parameter(Mandatory = $false)]
    [string]$ClientJar121,

    [Parameter(Mandatory = $false)]
    [Alias('BuildGlass')]
    [switch]$BuildAuraGlass,

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

# Validate release targets
$hasClientJars = [bool]([string]::IsNullOrWhiteSpace($ClientJar120) -eq $false -or [string]::IsNullOrWhiteSpace($ClientJar121) -eq $false)
if ($hasClientJars) {
    if ([string]::IsNullOrWhiteSpace($ClientJar120) -or [string]::IsNullOrWhiteSpace($ClientJar121)) {
        throw "Both -ClientJar120 and -ClientJar121 must be provided together."
    }
}

if (-not $hasClientJars -and -not $BuildAuraGlass) {
    throw "Nothing to release: provide -ClientJar120 and -ClientJar121, or specify -BuildAuraGlass."
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

# Strict Git environment and repository validation for PackDir (fail-closed, executed before worktree/build/writes)
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git command is required but not found in PATH."
}

# 1. Validate PackDir Git root
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$gitTopLevel = & git -C $resolvedPackDir rev-parse --show-toplevel 2>$null
$topExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap

if ($topExit -ne 0 -or [string]::IsNullOrWhiteSpace($gitTopLevel)) {
    throw "Git rev-parse failed on PackDir '$resolvedPackDir' with exit code $topExit. PackDir must be a valid Git repository root."
}
$resolvedGitTopLevel = (Resolve-Path -LiteralPath $gitTopLevel.Trim()).Path.TrimEnd('\')
if ($resolvedGitTopLevel -ne $resolvedPackDir) {
    throw "Security violation: PackDir '$resolvedPackDir' is not the root of its git repository (root is '$resolvedGitTopLevel')."
}

# 2. Validate Remote Origin matches exact expected repository: qutlawsoasis-debug/Aura-Pack
$ErrorActionPreference = 'Continue'
$gitRemoteOrigin = & git -C $resolvedPackDir remote get-url origin 2>$null
$remoteExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap

if ($remoteExit -ne 0 -or [string]::IsNullOrWhiteSpace($gitRemoteOrigin)) {
    throw "Git remote 'origin' is missing or failed on PackDir '$resolvedPackDir' with exit code $remoteExit."
}

$trimmedOrigin = $gitRemoteOrigin.Trim()
$allowedOriginPattern = '^(https?://github\.com/|git@github\.com:|ssh://git@github\.com/)qutlawsoasis-debug/Aura-Pack(\.git)?/?$'
if ($trimmedOrigin -notmatch $allowedOriginPattern) {
    throw "Security violation: PackDir git remote '$trimmedOrigin' does not match expected repository 'qutlawsoasis-debug/Aura-Pack'."
}

# 3. Validate clean Git worktree / index
$ErrorActionPreference = 'Continue'
$gitStatus = & git -C $resolvedPackDir status --porcelain 2>$null
$statusExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap

if ($statusExit -ne 0) {
    throw "Git status check failed on PackDir '$resolvedPackDir' with exit code $statusExit."
}
if (-not $isDryRun) {
    if ([string]::IsNullOrWhiteSpace($gitStatus) -eq $false) {
        $dirtyLines = @($gitStatus -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
        if ($dirtyLines.Count -gt 0) {
            throw "PackDir git index/worktree is not clean. Staged or uncommitted changes detected:`n$($dirtyLines -join "`n")"
        }
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

# Validate client jars input if provided
function Assert-SafeClientJar([string]$jarPath, [string]$paramName) {
    if (-not (Test-Path -LiteralPath $jarPath -PathType Leaf)) {
        throw "$paramName file not found: $jarPath"
    }
    $item = Get-Item -LiteralPath $jarPath -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Security violation: $paramName is a reparse point (junction/symlink): $jarPath"
    }
    $leaf = Split-Path -Leaf $jarPath
    if ($leaf -notmatch '^aura-client-.*\.jar$') {
        throw "$paramName filename must match 'aura-client-*.jar', got '$leaf'"
    }
}

if ($hasClientJars) {
    Assert-SafeClientJar $ClientJar120 "-ClientJar120"
    Assert-SafeClientJar $ClientJar121 "-ClientJar121"
}

# Verify required Java tool if building aura-glass
if ($BuildAuraGlass) {
    if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
        throw "java command is required to run GradleWrapperMain for aura-glass build."
    }
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

# Helper to validate and prepare manifest update/addition (fail-closed, strictly preserves formatting)
function Process-And-ValidateManifest(
    [string]$manifestFilePath,
    [string]$newVersion,
    [hashtable[]]$entries
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

    # 4. Regex update for root packVersion preserving formatting
    $pvRegex = [regex]::new('("packVersion"\s*:\s*")[^"]+(")')
    $pvMatches = $pvRegex.Matches($raw)
    if ($pvMatches.Count -ne 1) {
        throw "Manifest '$manifestFilePath' contains $($pvMatches.Count) matches for 'packVersion' property; expected exactly 1."
    }
    $updated = $pvRegex.Replace($raw, ('${1}' + $newVersion + '$2'), 1)

    $nl = if ($updated.Contains("`r`n")) { "`r`n" } else { "`n" }
    $replacedOldPaths = [System.Collections.Generic.List[string]]::new()
    $newlyAddedCount = 0

    foreach ($entry in $entries) {
        $targetPath = ($entry.Path -replace '\\', '/').Trim()
        $targetSha = "$($entry.Sha256)".Trim().ToLowerInvariant()
        $targetSize = [long]$entry.Size
        $targetMode = if ($entry.Mode) { "$($entry.Mode)".Trim() } else { "sync" }

        # Check existing entries via JSON parse of current text
        $curJson = $updated | ConvertFrom-Json
        $matching = @($curJson.files | Where-Object {
            $p = ($_.path -replace '\\', '/')
            if ($p -eq $targetPath) { return $true }
            if ($entry.Kind -eq 'client' -and $p -match '^mods/aura-client-.*\.jar$') { return $true }
            if ($entry.Kind -eq 'glass' -and $p -match '^mods/aura-glass-.*\.jar$') { return $true }
            return $false
        })

        if ($matching.Count -gt 1) {
            throw "Manifest '$manifestFilePath' contains $($matching.Count) entries matching '$targetPath'; expected at most 1."
        }

        if ($matching.Count -eq 1) {
            # UPDATE existing JSON block
            $existingPath = $matching[0].path
            if ($existingPath -ne $targetPath) {
                $replacedOldPaths.Add(($existingPath -replace '\\', '/').ToLowerInvariant())
            }

            $escapedOldPath = [regex]::Escape($existingPath)
            $blockRegex = [regex]::new('(\{[^{}]*?"path"\s*:\s*"' + $escapedOldPath + '"[^{}]*?\})', [System.Text.RegularExpressions.RegexOptions]::Singleline)
            $blockMatches = $blockRegex.Matches($updated)
            if ($blockMatches.Count -ne 1) {
                throw "Manifest '$manifestFilePath' contains $($blockMatches.Count) matching JSON blocks for '$existingPath'; expected exactly 1."
            }

            $blockMatch = $blockMatches[0]
            $blockText = $blockMatch.Groups[1].Value

            # Update path if filename changed
            $pathRegex = [regex]::new('("path"\s*:\s*")[^"]+(")')
            $replacedBlock = $pathRegex.Replace($blockText, ('${1}' + $targetPath + '$2'), 1)

            # Update sha256
            $shaRegex = [regex]::new('("sha256"\s*:\s*")[0-9a-fA-F]+(")')
            if ($shaRegex.IsMatch($replacedBlock)) {
                $replacedBlock = $shaRegex.Replace($replacedBlock, ('${1}' + $targetSha + '$2'), 1)
            } else {
                $pathPropRegex = [regex]::new('("path"\s*:\s*"[^"]*",?)')
                $replacedBlock = $pathPropRegex.Replace($replacedBlock, ('$1' + $nl + '      "sha256": "' + $targetSha + '",'), 1)
            }

            # Update size
            $sizeRegex = [regex]::new('("size"\s*:\s*)\d+')
            if ($sizeRegex.IsMatch($replacedBlock)) {
                $replacedBlock = $sizeRegex.Replace($replacedBlock, ('${1}' + $targetSize), 1)
            } else {
                $shaPropRegex = [regex]::new('("sha256"\s*:\s*"[^"]*",?)')
                $replacedBlock = $shaPropRegex.Replace($replacedBlock, ('$1' + $nl + '      "size": ' + $targetSize + ','), 1)
            }

            # Update mode
            $modeRegex = [regex]::new('("mode"\s*:\s*")[^"]+(")')
            if ($modeRegex.IsMatch($replacedBlock)) {
                $replacedBlock = $modeRegex.Replace($replacedBlock, ('${1}' + $targetMode + '$2'), 1)
            } else {
                $endBraceRegex = [regex]::new('(\r?\n\s*\})')
                $replacedBlock = $endBraceRegex.Replace($replacedBlock, (',' + $nl + '      "mode": "' + $targetMode + '"$1'), 1)
            }

            $updated = $updated.Substring(0, $blockMatch.Index) + $replacedBlock + $updated.Substring($blockMatch.Index + $blockMatch.Length)
        } else {
            # ADD new JSON block into files array
            $newlyAddedCount++
            $newBlock = "    {" + $nl +
                        "      `"path`": `"$targetPath`"," + $nl +
                        "      `"sha256`": `"$targetSha`"," + $nl +
                        "      `"size`": $targetSize," + $nl +
                        "      `"mode`": `"$targetMode`"" + $nl +
                        "    }"

            # Alphabetical insertion before the next entry
            $nextEntry = $curJson.files | Where-Object { ($_.path -replace '\\', '/') -gt $targetPath } | Select-Object -First 1
            if ($nextEntry) {
                $escapedNext = [regex]::Escape($nextEntry.path)
                $insertPattern = '([ \t]*\{[^{}]*?"path"\s*:\s*"' + $escapedNext + '"[^{}]*?\})'
                $updated = [regex]::Replace($updated, $insertPattern, ($newBlock + "," + $nl + '$1'), 1)
            } else {
                # Insert at end of files array before closing bracket
                $endPattern = '(\}[ \t]*)([\r\n]+[ \t]*\])'
                $endRegex = [regex]::new($endPattern, [System.Text.RegularExpressions.RegexOptions]::RightToLeft)
                if ($endRegex.IsMatch($updated)) {
                    $updated = $endRegex.Replace($updated, ('},' + $nl + $newBlock + '$2'), 1)
                } else {
                    # Empty files array: [ ]
                    $emptyPattern = '("files"\s*:\s*\[\s*\])'
                    $emptyRegex = [regex]::new($emptyPattern)
                    if ($emptyRegex.IsMatch($updated)) {
                        $updated = $emptyRegex.Replace($updated, ('"files": [' + $nl + $newBlock + $nl + '  ]'), 1)
                    } else {
                        throw "Manifest '$manifestFilePath' files array could not be updated with new entry '$targetPath'."
                    }
                }
            }
        }
    }

    # 5. Post-update JSON validation & semantic diff
    $newJson = $null
    try {
        $newJson = $updated | ConvertFrom-Json
    } catch {
        throw "Updated manifest text for '$manifestFilePath' failed JSON parse validation: $_"
    }

    if ($newJson.packVersion -ne $newVersion) {
        throw "Post-update validation failed: packVersion was '$($newJson.packVersion)', expected '$newVersion'."
    }

    # Verify each target entry in post-update JSON
    foreach ($entry in $entries) {
        $pNorm = ($entry.Path -replace '\\', '/').Trim()
        $newMatchingEntries = @($newJson.files | Where-Object {
            $p = $_.PSObject.Properties['path']
            $p -and (($p.Value -replace '\\', '/') -eq $pNorm)
        })
        if ($newMatchingEntries.Count -ne 1) {
            throw "Post-update validation failed: expected 1 target entry for '$pNorm', found $($newMatchingEntries.Count)."
        }

        $newTarget = $newMatchingEntries[0]
        $expectedSha = "$($entry.Sha256)".Trim().ToLowerInvariant()
        $expectedSize = [long]$entry.Size
        $expectedMode = if ($entry.Mode) { "$($entry.Mode)".Trim() } else { "sync" }

        if ($newTarget.sha256 -ne $expectedSha) {
            throw "Post-update validation failed: sha256 for '$pNorm' was '$($newTarget.sha256)', expected '$expectedSha'."
        }
        if ([long]$newTarget.size -ne $expectedSize) {
            throw "Post-update validation failed: size for '$pNorm' was '$($newTarget.size)', expected '$expectedSize'."
        }
        if ($newTarget.mode -ne $expectedMode) {
            throw "Post-update validation failed: mode for '$pNorm' was '$($newTarget.mode)', expected '$expectedMode'."
        }
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

    # Check files array length matches expected count
    $expectedCount = @($json.files).Count + $newlyAddedCount
    if (@($newJson.files).Count -ne $expectedCount) {
        throw "Post-update semantic diff failed: files count was $(@($newJson.files).Count), expected $expectedCount."
    }

    # Check that all non-target entries are preserved intact
    $targetPathList = @($entries | ForEach-Object { ($_.Path -replace '\\', '/').ToLowerInvariant() })
    foreach ($oldF in $json.files) {
        $oldNorm = ($oldF.path -replace '\\', '/').ToLowerInvariant()
        if ($targetPathList -contains $oldNorm -or $replacedOldPaths.Contains($oldNorm)) {
            continue
        }
        $matchingNew = @($newJson.files | Where-Object { ($_.path -replace '\\', '/').ToLowerInvariant() -eq $oldNorm })
        if ($matchingNew.Count -ne 1) {
            throw "Post-update semantic diff failed: non-target file entry '$($oldF.path)' missing or duplicated."
        }
        $newF = $matchingNew[0]
        if ($oldF.sha256 -ne $newF.sha256 -or [long]$oldF.size -ne [long]$newF.size -or $oldF.mode -ne $newF.mode) {
            throw "Post-update semantic diff failed: unexpected modification in non-target file entry '$($oldF.path)'."
        }
    }

    return $updated
}

$worktreeDir = $null
$stagingGuid = [Guid]::NewGuid().ToString("N")
$stagingDir = Join-Path $env:TEMP "aura-pack-stage-$stagingGuid"

try {
    # 7. Build aura-glass in worktree only if requested
    $glassJar120 = $null
    $glassJar121 = $null
    $glassSha120 = $null
    $glassSize120 = 0
    $glassSha121 = $null
    $glassSize121 = 0

    if ($BuildAuraGlass) {
        $worktreeGuid = [Guid]::NewGuid().ToString("N")
        $worktreeDir = Join-Path $env:TEMP "aura-glass-build-$worktreeGuid"
        Write-Host "Creating clean worktree from $LauncherRef at $worktreeDir..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $launcherRoot worktree add --detach $worktreeDir $LauncherRef
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create git worktree for ref '$LauncherRef'"
        }

        Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass")
        Invoke-GradleBuild (Join-Path $worktreeDir "aura-glass-1.21")

        $glassJar120 = Join-Path $worktreeDir "aura-glass\build\libs\aura-glass-1.0.0.jar"
        $glassJar121 = Join-Path $worktreeDir "aura-glass-1.21\build\libs\aura-glass-1.0.0.jar"

        if (-not (Test-Path -LiteralPath $glassJar120)) {
            throw "Expected jar not found: $glassJar120"
        }
        if (-not (Test-Path -LiteralPath $glassJar121)) {
            throw "Expected jar not found: $glassJar121"
        }

        $glassSha120 = (Get-FileHash -Path $glassJar120 -Algorithm SHA256).Hash.ToLowerInvariant()
        $glassSize120 = (Get-Item -LiteralPath $glassJar120).Length

        $glassSha121 = (Get-FileHash -Path $glassJar121 -Algorithm SHA256).Hash.ToLowerInvariant()
        $glassSize121 = (Get-Item -LiteralPath $glassJar121).Length
    }

    # 8. Resolve client jars if provided
    $clientResolved120 = $null
    $clientResolved121 = $null
    $clientLeaf120 = $null
    $clientLeaf121 = $null
    $clientSha120 = $null
    $clientSize120 = 0
    $clientSha121 = $null
    $clientSize121 = 0

    if ($hasClientJars) {
        $clientResolved120 = (Resolve-Path -LiteralPath $ClientJar120).Path
        $clientResolved121 = (Resolve-Path -LiteralPath $ClientJar121).Path
        $clientLeaf120 = Split-Path -Leaf $clientResolved120
        $clientLeaf121 = Split-Path -Leaf $clientResolved121

        $clientSha120 = (Get-FileHash -Path $clientResolved120 -Algorithm SHA256).Hash.ToLowerInvariant()
        $clientSize120 = (Get-Item -LiteralPath $clientResolved120).Length

        $clientSha121 = (Get-FileHash -Path $clientResolved121 -Algorithm SHA256).Hash.ToLowerInvariant()
        $clientSize121 = (Get-Item -LiteralPath $clientResolved121).Length
    }

    # 9. Compute common UTC packVersion
    $packVersion = (Get-Date).ToUniversalTime().ToString("yyyyMMdd.HHmm")

    Write-Host "`nTarget Pack Version: $packVersion" -ForegroundColor Green
    if ($hasClientJars) {
        Write-Host "aura-client (1.20.1): mods/$clientLeaf120 (sha256=$clientSha120, size=$clientSize120)" -ForegroundColor Green
        Write-Host "aura-client (1.21.1): versions/1.21.1/mods/$clientLeaf121 (sha256=$clientSha121, size=$clientSize121)" -ForegroundColor Green
    }
    if ($BuildAuraGlass) {
        Write-Host "aura-glass (1.20.1):  mods/aura-glass-1.0.0.jar (sha256=$glassSha120, size=$glassSize120)" -ForegroundColor Green
        Write-Host "aura-glass (1.21.1):  versions/1.21.1/mods/aura-glass-1.0.0.jar (sha256=$glassSha121, size=$glassSize121)" -ForegroundColor Green
    }

    # 10. Prepare entries and validate/update manifests in memory (atomic, fail-closed)
    $entries120 = [System.Collections.Generic.List[hashtable]]::new()
    $entries121 = [System.Collections.Generic.List[hashtable]]::new()

    if ($hasClientJars) {
        $entries120.Add(@{
            Path = "mods/$clientLeaf120"
            Sha256 = $clientSha120
            Size = $clientSize120
            Mode = "sync"
            Kind = "client"
        })
        $entries121.Add(@{
            Path = "mods/$clientLeaf121"
            Sha256 = $clientSha121
            Size = $clientSize121
            Mode = "sync"
            Kind = "client"
        })
    }

    if ($BuildAuraGlass) {
        $entries120.Add(@{
            Path = "mods/aura-glass-1.0.0.jar"
            Sha256 = $glassSha120
            Size = $glassSize120
            Mode = "sync"
            Kind = "glass"
        })
        $entries121.Add(@{
            Path = "mods/aura-glass-1.0.0.jar"
            Sha256 = $glassSha121
            Size = $glassSize121
            Mode = "sync"
            Kind = "glass"
        })
    }

    $updatedManifestContent120 = Process-And-ValidateManifest -manifestFilePath $manifest120 -newVersion $packVersion -entries $entries120
    $updatedManifestContent121 = Process-And-ValidateManifest -manifestFilePath $manifest121 -newVersion $packVersion -entries $entries121

    # 11. Prepare isolated staging copy in temp (PackDir is untouched)
    Write-Host "Creating staging copy in $stagingDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    # Copy files from PackDir into stagingDir (excluding .git)
    Get-ChildItem -LiteralPath $resolvedPackDir -Force | Where-Object { $_.Name -ne '.git' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stagingDir $_.Name) -Recurse -Force
    }

    # Place new jars and manifests into staging copy
    if ($hasClientJars) {
        $stagingClientJar120 = Join-Path $stagingDir "mods\$clientLeaf120"
        $stagingClientJar121 = Join-Path $stagingDir "versions\1.21.1\mods\$clientLeaf121"

        # Clean old aura-client jars with different names in staging
        Get-ChildItem -Path (Join-Path $stagingDir "mods") -Filter "aura-client-*.jar" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $clientLeaf120 } | Remove-Item -Force
        Get-ChildItem -Path (Join-Path $stagingDir "versions\1.21.1\mods") -Filter "aura-client-*.jar" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $clientLeaf121 } | Remove-Item -Force

        $null = New-Item -ItemType Directory -Path (Split-Path $stagingClientJar120 -Parent) -Force -ErrorAction SilentlyContinue
        $null = New-Item -ItemType Directory -Path (Split-Path $stagingClientJar121 -Parent) -Force -ErrorAction SilentlyContinue

        Copy-Item -LiteralPath $clientResolved120 -Destination $stagingClientJar120 -Force
        Copy-Item -LiteralPath $clientResolved121 -Destination $stagingClientJar121 -Force
    }

    if ($BuildAuraGlass) {
        $stagingGlassJar120 = Join-Path $stagingDir "mods\aura-glass-1.0.0.jar"
        $stagingGlassJar121 = Join-Path $stagingDir "versions\1.21.1\mods\aura-glass-1.0.0.jar"
        $null = New-Item -ItemType Directory -Path (Split-Path $stagingGlassJar120 -Parent) -Force -ErrorAction SilentlyContinue
        $null = New-Item -ItemType Directory -Path (Split-Path $stagingGlassJar121 -Parent) -Force -ErrorAction SilentlyContinue

        Copy-Item -LiteralPath $glassJar120 -Destination $stagingGlassJar120 -Force
        Copy-Item -LiteralPath $glassJar121 -Destination $stagingGlassJar121 -Force
    }

    $stagingManifest120 = Join-Path $stagingDir "manifest.json"
    $stagingManifest121 = Join-Path $stagingDir "manifest-1.21.1.json"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($stagingManifest120, $updatedManifestContent120, $utf8NoBom)
    [System.IO.File]::WriteAllText($stagingManifest121, $updatedManifestContent121, $utf8NoBom)

    # 12. Run parity check in isolated staging environment
    $stagingParityScript = Join-Path $stagingDir "tools\check-parity.ps1"
    $stagingExceptions = Join-Path $stagingDir "tools\parity-exceptions.json"
    Write-Host "Running check-parity.ps1 in staging environment..." -ForegroundColor Cyan
    & $stagingParityScript -Root120 $stagingDir -Root121 (Join-Path $stagingDir "versions\1.21.1") -ExceptionsFile $stagingExceptions
    if ($LASTEXITCODE -ne 0) {
        throw "PackDir parity check failed in staging with exit code $LASTEXITCODE"
    }

    # 13. Handle DryRun vs Live Release
    if ($isDryRun) {
        Write-Host "`n==================================================" -ForegroundColor Green
        Write-Host "                 DRY-RUN PLAN & DIFF              " -ForegroundColor Green
        Write-Host "==================================================" -ForegroundColor Green
        Write-Host "LauncherRef: $LauncherRef"
        Write-Host "PackDir:     $resolvedPackDir"
        Write-Host "packVersion: $packVersion"
        if ($hasClientJars) {
            Write-Host "1.20.1 Client: mods/$clientLeaf120 ($clientSize120 bytes, sha256: $clientSha120)"
            Write-Host "1.21.1 Client: versions/1.21.1/mods/$clientLeaf121 ($clientSize121 bytes, sha256: $clientSha121)"
        }
        if ($BuildAuraGlass) {
            Write-Host "1.20.1 Glass:  mods/aura-glass-1.0.0.jar ($glassSize120 bytes, sha256: $glassSha120)"
            Write-Host "1.21.1 Glass:  versions/1.21.1/mods/aura-glass-1.0.0.jar ($glassSize121 bytes, sha256: $glassSha121)"
        }
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
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $gitStatus = & git -C $resolvedPackDir status --porcelain 2>$null
        $statusExit = $LASTEXITCODE
        $ErrorActionPreference = $prevEap

        if ($statusExit -ne 0) {
            throw "Git status check failed on PackDir '$resolvedPackDir' with exit code $statusExit."
        }
        if ([string]::IsNullOrWhiteSpace($gitStatus) -eq $false) {
            $dirtyLines = @($gitStatus -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
            if ($dirtyLines.Count -gt 0) {
                throw "PackDir git index/worktree is not clean. Refusing to release with existing modifications:`n$($dirtyLines -join "`n")"
            }
        }

        $branchName = if ($hasClientJars -and -not $BuildAuraGlass) {
            "release/aura-client-$packVersion"
        } elseif ($BuildAuraGlass -and -not $hasClientJars) {
            "release/aura-glass-$packVersion"
        } else {
            "release/aura-pack-$packVersion"
        }

        Write-Host "Creating branch $branchName in $resolvedPackDir..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir checkout -b $branchName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create branch $branchName in $resolvedPackDir"
        }

        $expectedFilesList = [System.Collections.Generic.List[string]]::new()
        $expectedFilesList.Add('manifest-1.21.1.json')
        $expectedFilesList.Add('manifest.json')
        $removedOldFiles = [System.Collections.Generic.List[string]]::new()

        # Copy client jars to PackDir if provided
        if ($hasClientJars) {
            $destClientJar120 = Join-Path $resolvedPackDir "mods\$clientLeaf120"
            $destClientJar121 = Join-Path $resolvedPackDir "versions\1.21.1\mods\$clientLeaf121"
            Assert-SafePathInsidePackDir $destClientJar120 $resolvedPackDir
            Assert-SafePathInsidePackDir $destClientJar121 $resolvedPackDir

            # Remove older aura-client jars from git if names differ
            $oldJars120 = @(Get-ChildItem -Path (Join-Path $resolvedPackDir "mods") -Filter "aura-client-*.jar" -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne $clientLeaf120 })
            foreach ($oj in $oldJars120) {
                git -c core.longpaths=true -C $resolvedPackDir rm -f "mods/$($oj.Name)" 2>$null
                if (Test-Path -LiteralPath $oj.FullName) { Remove-Item -LiteralPath $oj.FullName -Force }
                $removedOldFiles.Add("mods/$($oj.Name)")
            }

            $oldJars121 = @(Get-ChildItem -Path (Join-Path $resolvedPackDir "versions\1.21.1\mods") -Filter "aura-client-*.jar" -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne $clientLeaf121 })
            foreach ($oj in $oldJars121) {
                git -c core.longpaths=true -C $resolvedPackDir rm -f "versions/1.21.1/mods/$($oj.Name)" 2>$null
                if (Test-Path -LiteralPath $oj.FullName) { Remove-Item -LiteralPath $oj.FullName -Force }
                $removedOldFiles.Add("versions/1.21.1/mods/$($oj.Name)")
            }

            $null = New-Item -ItemType Directory -Path (Split-Path $destClientJar120 -Parent) -Force -ErrorAction SilentlyContinue
            $null = New-Item -ItemType Directory -Path (Split-Path $destClientJar121 -Parent) -Force -ErrorAction SilentlyContinue

            Copy-Item -LiteralPath $clientResolved120 -Destination $destClientJar120 -Force
            Copy-Item -LiteralPath $clientResolved121 -Destination $destClientJar121 -Force

            $expectedFilesList.Add("mods/$clientLeaf120")
            $expectedFilesList.Add("versions/1.21.1/mods/$clientLeaf121")
        }

        # Copy glass jars to PackDir if built
        if ($BuildAuraGlass) {
            $destGlassJar120 = Join-Path $resolvedPackDir "mods\aura-glass-1.0.0.jar"
            $destGlassJar121 = Join-Path $resolvedPackDir "versions\1.21.1\mods\aura-glass-1.0.0.jar"
            Assert-SafePathInsidePackDir $destGlassJar120 $resolvedPackDir
            Assert-SafePathInsidePackDir $destGlassJar121 $resolvedPackDir

            $null = New-Item -ItemType Directory -Path (Split-Path $destGlassJar120 -Parent) -Force -ErrorAction SilentlyContinue
            $null = New-Item -ItemType Directory -Path (Split-Path $destGlassJar121 -Parent) -Force -ErrorAction SilentlyContinue

            Copy-Item -LiteralPath $glassJar120 -Destination $destGlassJar120 -Force
            Copy-Item -LiteralPath $glassJar121 -Destination $destGlassJar121 -Force

            $expectedFilesList.Add('mods/aura-glass-1.0.0.jar')
            $expectedFilesList.Add('versions/1.21.1/mods/aura-glass-1.0.0.jar')
        }

        # Write updated manifests to PackDir
        Assert-SafePathInsidePackDir $manifest120 $resolvedPackDir
        Assert-SafePathInsidePackDir $manifest121 $resolvedPackDir
        [System.IO.File]::WriteAllText($manifest120, $updatedManifestContent120, $utf8NoBom)
        [System.IO.File]::WriteAllText($manifest121, $updatedManifestContent121, $utf8NoBom)

        $filesToStage = @($expectedFilesList | Sort-Object -Unique)
        $allExpectedStaged = @($expectedFilesList + $removedOldFiles | Sort-Object -Unique)

        Write-Host "Staging release artifacts..." -ForegroundColor Cyan
        git -c core.longpaths=true -C $resolvedPackDir add -- $filesToStage
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stage release artifacts in $resolvedPackDir"
        }

        # Verify staged files strictly match expected set
        $stagedFiles = @(git -c core.longpaths=true -C $resolvedPackDir diff --cached --name-only)
        $stagedNormalized = @($stagedFiles | ForEach-Object { ($_ -replace '\\', '/').Trim() } | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)

        if ($stagedNormalized.Count -ne $allExpectedStaged.Count -or ($stagedNormalized -join ';') -ne ($allExpectedStaged -join ';')) {
            throw "Security violation: Staged index contains unexpected files! Expected strictly [$($allExpectedStaged -join ', ')], but found: [$($stagedNormalized -join ', ')]"
        }

        $commitMsg = if ($hasClientJars -and -not $BuildAuraGlass) {
            "release: update aura-client to $packVersion"
        } elseif ($BuildAuraGlass -and -not $hasClientJars) {
            "release: update aura-glass to $packVersion"
        } else {
            "release: update aura-client and aura-glass to $packVersion"
        }

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
            $artifactBullets = [System.Collections.Generic.List[string]]::new()
            if ($hasClientJars) {
                $artifactBullets.Add("- **1.20.1 aura-client**: ``mods/$clientLeaf120```n  - SHA256: ``$clientSha120```n  - Size: $clientSize120 bytes")
                $artifactBullets.Add("- **1.21.1 aura-client**: ``versions/1.21.1/mods/$clientLeaf121```n  - SHA256: ``$clientSha121```n  - Size: $clientSize121 bytes")
            }
            if ($BuildAuraGlass) {
                $artifactBullets.Add("- **1.20.1 aura-glass**: ``mods/aura-glass-1.0.0.jar```n  - SHA256: ``$glassSha120```n  - Size: $glassSize120 bytes")
                $artifactBullets.Add("- **1.21.1 aura-glass**: ``versions/1.21.1/mods/aura-glass-1.0.0.jar```n  - SHA256: ``$glassSha121```n  - Size: $glassSize121 bytes")
            }
            $prBody = @"
## Summary
Automated release of $commitMsg.

### Artifacts:
$($artifactBullets -join "`n")

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
