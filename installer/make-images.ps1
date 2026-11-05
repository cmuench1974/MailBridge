# Generates the installer dialog images from the app icon (only needed after the icon changes):
#   assets\InstallerDialog.bmp  493 x 312  Welcome/Finish: dark strip on the left with icon, room for text on the right
#   assets\InstallerBanner.bmp  493 x 58   Header of the other dialogs: icon on the right
# Run with: powershell -NoProfile -ExecutionPolicy Bypass -File installer\make-images.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$assets = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# Use the 256 px frame of the .ico. It is stored PNG-compressed, which System.Drawing.Icon cannot read
# (it falls back to 64 px), so read the frame from the ICONDIR entries and decode it directly.
function Get-LargestIconFrame([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $best = $null
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + 16 * $i
        $size = if ($bytes[$entry] -eq 0) { 256 } else { $bytes[$entry] }
        if (-not $best -or $size -gt $best.Size) {
            $best = @{ Size = $size; Length = [BitConverter]::ToInt32($bytes, $entry + 8); Offset = [BitConverter]::ToInt32($bytes, $entry + 12) }
        }
    }
    $stream = [IO.MemoryStream]::new($bytes, $best.Offset, $best.Length)
    try { return [System.Drawing.Bitmap]::new($stream) }
    catch { return [System.Drawing.Icon]::new($path, 256, 256).ToBitmap() }  # BMP-encoded frame
}

$icon = Get-LargestIconFrame (Join-Path $root 'src\MailBridge.App\Assets\AppIcon.ico')

function New-Canvas([int]$w, [int]$h) {
    $bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::White)
    return $bmp, $g
}

# Dialog: 164 px strip on the left in the app's dark navy, amber accent line from the icon, icon and name
$bmp, $g = New-Canvas 493 312
$strip = [System.Drawing.Rectangle]::new(0, 0, 164, 312)
$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($strip,
    [System.Drawing.ColorTranslator]::FromHtml('#1E293B'), [System.Drawing.ColorTranslator]::FromHtml('#0B1220'), 90)
$g.FillRectangle($gradient, $strip)
$g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F59E0B')), 164, 0, 3, 312)
$g.DrawImage($icon, 26, 70, 112, 112)
$font = [System.Drawing.Font]::new('Segoe UI Semibold', 13)
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = 'Center'
$g.DrawString('MailBridge', $font, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, 196, 164, 30), $format)
$bmp.Save((Join-Path $assets 'InstallerDialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose()

# Banner: white (the dialog text sits on the left), icon on the right
$bmp, $g = New-Canvas 493 58
$g.DrawImage($icon, 493 - 52, 5, 48, 48)
$bmp.Save((Join-Path $assets 'InstallerBanner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose()

Write-Host "Images written to $assets"
