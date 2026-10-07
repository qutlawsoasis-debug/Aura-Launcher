# scripts/check-design.ps1
# Проверка соответствия UI правилам DESIGN.md во Views и Controls

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $repoRoot

$targetDirs = @("Views", "Controls") | Where-Object { Test-Path (Join-Path $repoRoot $_) }
if ($targetDirs.Count -eq 0) {
    Write-Output "Папки Views и Controls не найдены."
    exit 0
}

$files = Get-ChildItem -Path $targetDirs -Include *.xaml, *.cs -Recurse | Where-Object { -not $_.PSIsContainer }

$rule1Count = 0
$rule2Count = 0
$rule3Count = 0
$rule4Count = 0
$rule5Count = 0

$allFindings = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $relPath = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)

    for ($lineIdx = 0; $lineIdx -lt $lines.Length; $lineIdx++) {
        $lineNum = $lineIdx + 1
        $line = $lines[$lineIdx]
        $trimmed = $line.Trim()

        # -----------------------------------------------------------------
        # Правило 1: Эмодзи и символы из диапазонов U+1F300–U+1FAFF и U+2600–U+27BF
        # -----------------------------------------------------------------
        $foundChars = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt $line.Length; $i++) {
            $cp = [char]::ConvertToUtf32($line, $i)
            $isSurr = ($line[$i] -ge [char]0xD800 -and $line[$i] -le [char]0xDBFF)
            if (($cp -ge 0x1F300 -and $cp -le 0x1FAFF) -or ($cp -ge 0x2600 -and $cp -le 0x27BF)) {
                $charStr = if ($isSurr) { $line.Substring($i, 2) } else { $line.Substring($i, 1) }
                $foundChars.Add($charStr)
            }
            if ($isSurr) { $i++ }
        }
        if ($foundChars.Count -gt 0) {
            $rule1Count++
            $frag = ($foundChars | Select-Object -Unique) -join " "
            $allFindings.Add("${relPath}:${lineNum}: Правило 1: $frag")
        }

        # -----------------------------------------------------------------
        # Правило 2: Жёсткие цвета вида #RRGGBB / #AARRGGBB в Views
        # -----------------------------------------------------------------
        if ($relPath -notlike "*Themes/Colors.xaml*" -and $relPath -notlike "*Themes\Colors.xaml*") {
            $hexMatches = [regex]::Matches($line, '#([0-9a-fA-F]{8}|[0-9a-fA-F]{6})\b')
            if ($hexMatches.Count -gt 0) {
                $rule2Count++
                $frag = ($hexMatches | ForEach-Object { $_.Value } | Select-Object -Unique) -join ", "
                $allFindings.Add("${relPath}:${lineNum}: Правило 2: $frag")
            }
        }

        # -----------------------------------------------------------------
        # Правило 3: FontFamily, отличный от Oswald и Onest; любое упоминание Inter
        # -----------------------------------------------------------------
        if ($line -match '\bInter\b') {
            $rule3Count++
            $allFindings.Add("${relPath}:${lineNum}: Правило 3: Inter")
        } elseif ($line -match 'FontFamily\s*=\s*["'']([^"'']+)["'']') {
            $val = $matches[1]
            if ($val -notmatch 'Oswald' -and $val -notmatch 'Onest' -and $val -notmatch 'HeadingFont' -and $val -notmatch 'MainFont') {
                $rule3Count++
                $allFindings.Add("${relPath}:${lineNum}: Правило 3: FontFamily=""$val""")
            }
        }

        # -----------------------------------------------------------------
        # Правило 4: DropShadowEffect и BlurEffect со свечением (Color из amber)
        # -----------------------------------------------------------------
        if (($line -match 'DropShadowEffect|BlurEffect') -and ($line -match '#F2A63C|#EB962B|#F7B44F|#A85F12|Ember|Amber')) {
            $rule4Count++
            $allFindings.Add("${relPath}:${lineNum}: Правило 4: $trimmed")
        }

        # -----------------------------------------------------------------
        # Правило 5: Стили Button/TextBox/ListBox, объявленные локально в View
        # -----------------------------------------------------------------
        if ($line -match '<Style\b[^>]*\bTargetType\s*=\s*["''](\{x:Type\s+)?(Button|TextBox|ListBox)\}?["'']') {
            $rule5Count++
            $allFindings.Add("${relPath}:${lineNum}: Правило 5: $trimmed")
        } elseif ($line -match '<(Button|TextBox|ListBox)\.Style>') {
            $rule5Count++
            $allFindings.Add("${relPath}:${lineNum}: Правило 5: $trimmed")
        }
    }
}

foreach ($finding in $allFindings) {
    Write-Output $finding
}

Write-Output ""
Write-Output "Итоги по правилам:"
Write-Output "Правило 1 (Эмодзи и символы U+1F300-U+1FAFF, U+2600-U+27BF): $rule1Count"
Write-Output "Правило 2 (Жёсткие цвета #RRGGBB / #AARRGGBB): $rule2Count"
Write-Output "Правило 3 (FontFamily не Oswald/Onest, Inter): $rule3Count"
Write-Output "Правило 4 (DropShadowEffect/BlurEffect с amber-свечением): $rule4Count"
Write-Output "Правило 5 (Локальные стили Button/TextBox/ListBox): $rule5Count"
Write-Output "Всего находок: $($allFindings.Count)"

if ($allFindings.Count -gt 0) {
    exit 1
} else {
    exit 0
}
