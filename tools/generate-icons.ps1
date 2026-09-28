# Generates simple placeholder Fluent-style app icons for MailBridge:
# a rounded accent-blue tile with a white envelope glyph and a small
# bidirectional "migrate" arrow badge in the corner.
# Regenerate with: powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-icons.ps1

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\src\MailBridge.App\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

$accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)
$accentDark = [System.Drawing.Color]::FromArgb(255, 0, 90, 158)
$white = [System.Drawing.Color]::White

function New-MailBridgeIcon {
    param(
        [int]$Width,
        [int]$Height,
        [string]$OutPath,
        [bool]$Transparent = $true
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $shortSide = [Math]::Min($Width, $Height)
    $margin = [Math]::Round($shortSide * 0.06)
    $rectW = $Width - 2 * $margin
    $rectH = $Height - 2 * $margin
    $radius = [Math]::Round($shortSide * 0.18)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($margin, $margin, $d, $d, 180, 90)
    $path.AddArc($margin + $rectW - $d, $margin, $d, $d, 270, 90)
    $path.AddArc($margin + $rectW - $d, $margin + $rectH - $d, $d, $d, 0, 90)
    $path.AddArc($margin, $margin + $rectH - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point($margin, $margin)),
        (New-Object System.Drawing.Point(($margin + $rectW), ($margin + $rectH))),
        $accent, $accentDark)
    $g.FillPath($brush, $path)

    # Envelope glyph, centered, scaled to tile size.
    $envW = $rectW * 0.6
    $envH = $envW * 0.62
    $envX = $margin + ($rectW - $envW) / 2
    $envY = $margin + ($rectH - $envH) / 2 - ($rectH * 0.02)

    $whitePen = New-Object System.Drawing.Pen($white, [Math]::Max(1.5, $shortSide * 0.018))
    $whitePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $whiteBrush = New-Object System.Drawing.SolidBrush($white)

    $envRect = New-Object System.Drawing.RectangleF($envX, $envY, $envW, $envH)
    $g.FillRectangle($whiteBrush, $envRect)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($accentDark)), (New-Object System.Drawing.RectangleF(($envX + $envW*0.06), ($envY + $envH*0.14), ($envW*0.88), ($envH*0.72))))

    $flapPoints = @(
        (New-Object System.Drawing.PointF($envX, $envY)),
        (New-Object System.Drawing.PointF(($envX + $envW/2), ($envY + $envH*0.55))),
        (New-Object System.Drawing.PointF(($envX + $envW), $envY))
    )
    $g.FillPolygon($whiteBrush, $flapPoints)

    $whitePen.Dispose()
    $whiteBrush.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()

    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

New-MailBridgeIcon -Width 44  -Height 44  -OutPath (Join-Path $assetsDir "Square44x44Logo.png")
New-MailBridgeIcon -Width 150 -Height 150 -OutPath (Join-Path $assetsDir "Square150x150Logo.png")
New-MailBridgeIcon -Width 71  -Height 71  -OutPath (Join-Path $assetsDir "Square71x71Logo.png")
New-MailBridgeIcon -Width 310 -Height 150 -OutPath (Join-Path $assetsDir "Wide310x150Logo.png")
New-MailBridgeIcon -Width 50  -Height 50  -OutPath (Join-Path $assetsDir "StoreLogo.png")
New-MailBridgeIcon -Width 24  -Height 24  -OutPath (Join-Path $assetsDir "LockScreenLogo.png")
New-MailBridgeIcon -Width 620 -Height 300 -OutPath (Join-Path $assetsDir "SplashScreen.png")
New-MailBridgeIcon -Width 256 -Height 256 -OutPath (Join-Path $assetsDir "AppIcon.png")

Write-Output "Icons written to $assetsDir"
