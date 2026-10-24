param(
    [ValidateSet('shot', 'click', 'kill')]
    [string]$Action = 'shot',
    [int]$X = 0,
    [int]$Y = 0,
    [string]$OutFile = "$PSScriptRoot\..\docs\redesign\qa\gallery\step.png",
    [switch]$Offscreen
)

$isOffscreen = $Offscreen -or ($env:AURA_OFFSCREEN -eq '1')
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class QaWin {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
}
'@ -ReferencedAssemblies System.Drawing

function Get-AuraWindow {
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process -Name AuraLauncher -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq 'Aura Launcher' } | Select-Object -First 1
        if ($p) { return $p }
        Start-Sleep -Milliseconds 500
    }
    throw 'окно Aura Launcher не найдено'
}

$p = Get-AuraWindow
$hwnd = $p.MainWindowHandle

if (-not $isOffscreen) {
    [QaWin]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 400
}

switch ($Action) {
    'shot' {
        $rc = New-Object QaWin+RECT
        [QaWin]::GetWindowRect($hwnd, [ref]$rc) | Out-Null
        $w = $rc.Right - $rc.Left; $h = $rc.Bottom - $rc.Top
        $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        [QaWin]::PrintWindow($hwnd, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $g.Dispose()
        $OutFile = [IO.Path]::GetFullPath($OutFile)
        New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
        $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "shot rect=$($rc.Left),$($rc.Top) ${w}x${h} -> $OutFile"
    }
    'click' {
        if ($isOffscreen) {
            Write-Warning "Click via mouse_event is disabled in offscreen mode to prevent stealing user cursor. Use JS/CDP navigation instead."
            exit 0
        }
        $rc = New-Object QaWin+RECT
        [QaWin]::GetWindowRect($hwnd, [ref]$rc) | Out-Null
        $sx = $rc.Left + $X; $sy = $rc.Top + $Y
        [QaWin]::SetCursorPos($sx, $sy) | Out-Null
        Start-Sleep -Milliseconds 200
        [QaWin]::mouse_event([QaWin]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        [QaWin]::mouse_event([QaWin]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Write-Host "clicked client $X,$Y -> screen $sx,$sy (rect $($rc.Left),$($rc.Top))"
    }
    'kill' {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Write-Host "killed $($p.Id)"
    }
}
