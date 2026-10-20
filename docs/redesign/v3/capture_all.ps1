$chromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$outDir = "$PSScriptRoot\screenshots\iteration-01"
$tempUserDir = "$env:TEMP\chrome_shot_v3"

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$screens = @("play", "lobby", "friends", "wardrobe", "workshop", "settings")
$viewports = @(
    @{ Name = "1920x1080"; W = 1920; H = 1080 },
    @{ Name = "1366x768";  W = 1366; H = 768 }
)

$baseFile = "file:///$($PSScriptRoot.Replace('\', '/'))/index.html"

foreach ($vp in $viewports) {
    foreach ($screen in $screens) {
        $outFile = "$outDir\$screen-$($vp.Name).png"
        $url = "$baseFile#/$screen"
        Write-Host "Capturing $screen at $($vp.Name)..."
        $args = @(
            "--headless=new",
            "--user-data-dir=$tempUserDir",
            "--window-size=$($vp.W),$($vp.H)",
            "--virtual-time-budget=2500",
            "--hide-scrollbars",
            "--screenshot=$outFile",
            $url
        )
        Start-Process -FilePath $chromePath -ArgumentList $args -Wait
    }
}

# Capture low effects mode
$outFileLow = "$outDir\play-1920x1080-effects-low.png"
Write-Host "Capturing play with effects=low..."
$argsLow = @(
    "--headless=new",
    "--user-data-dir=$tempUserDir",
    "--window-size=1920,1080",
    "--virtual-time-budget=2500",
    "--hide-scrollbars",
    "--screenshot=$outFileLow",
    "$baseFile?effects=low#/play"
)
Start-Process -FilePath $chromePath -ArgumentList $argsLow -Wait

# Clean up temp profile
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $tempUserDir

Write-Host "All screenshots captured successfully!"
