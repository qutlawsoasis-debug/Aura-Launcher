Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path "app_icon.png"))
$w = $src.Width
$h = $src.Height

# Make background color outside transparent by checking alpha and dark border
$bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $c = $src.GetPixel($x, $y)
        # If very close to dark background #010302 or (R < 10 and G < 10 and B < 10)
        if ($c.R -lt 12 -and $c.G -lt 14 -and $c.B -lt 12) {
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        } else {
            $bmp.SetPixel($x, $y, $c)
        }
    }
}
$src.Dispose()

$targetSplash = New-Object System.Drawing.Bitmap 480, 320
$g = [System.Drawing.Graphics]::FromImage($targetSplash)
$bg = [System.Drawing.ColorTranslator]::FromHtml('#0B141A')
$g.Clear($bg)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$destW = 96
$destH = 96
$destX = [int]((480 - $destW) / 2)
$destY = [int]((320 - $destH) / 2)
$g.DrawImage($bmp, $destX, $destY, $destW, $destH)

$g.Dispose()
$bmp.Dispose()

$splashPath = Join-Path (Get-Location).Path "splash.png"
$targetSplash.Save($splashPath, [System.Drawing.Imaging.ImageFormat]::Png)
$targetSplash.Dispose()
Write-Host "splash.png generated cleanly"
