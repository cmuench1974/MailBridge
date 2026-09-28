# Generates the MailBridge app icons: a rounded accent-blue tile with a
# white envelope glyph and a small bidirectional "migrate" arrow badge,
# written as PNGs (MSIX assets) and a multi-size app.ico (exe + window icon).
# Regenerate with: powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-icons.ps1

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\src\MailBridge.App\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

$accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)
$accentDark = [System.Drawing.Color]::FromArgb(255, 0, 90, 158)
$white = [System.Drawing.Color]::White

function Draw-MailBridgeGlyph {
    param(
        [System.Drawing.Graphics]$G,
        [int]$Width,
        [int]$Height
    )

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
    $G.FillPath($brush, $path)

    # Envelope glyph, centered, scaled to tile size.
    $envW = $rectW * 0.6
    $envH = $envW * 0.62
    $envX = $margin + ($rectW - $envW) / 2
    $envY = $margin + ($rectH - $envH) / 2 - ($rectH * 0.02)

    $whitePen = New-Object System.Drawing.Pen($white, [Math]::Max(1.5, $shortSide * 0.018))
    $whitePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $whiteBrush = New-Object System.Drawing.SolidBrush($white)

    $envRect = New-Object System.Drawing.RectangleF($envX, $envY, $envW, $envH)
    $G.FillRectangle($whiteBrush, $envRect)
    $G.FillRectangle((New-Object System.Drawing.SolidBrush($accentDark)), (New-Object System.Drawing.RectangleF(($envX + $envW*0.06), ($envY + $envH*0.14), ($envW*0.88), ($envH*0.72))))


    $flapPoints = @(
        (New-Object System.Drawing.PointF($envX, $envY)),
        (New-Object System.Drawing.PointF(($envX + $envW/2), ($envY + $envH*0.55))),
        (New-Object System.Drawing.PointF(($envX + $envW), $envY))
    )
    $G.FillPolygon($whiteBrush, $flapPoints)

    # Migration badge: two opposing arrows in the top-right corner.
    if ($shortSide -ge 44) {
        $badgeR = $shortSide * 0.17
        $badgeX = $margin + $rectW - $badgeR
        $badgeY = $margin
        $badgeBrush = New-Object System.Drawing.SolidBrush($accentDark)
        $G.FillEllipse($badgeBrush, $badgeX - $badgeR, $badgeY - $badgeR, $badgeR * 2, $badgeR * 2)
        $arrowPen = New-Object System.Drawing.Pen($white, [Math]::Max(2, $shortSide * 0.022))
        $arrowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
        $arrowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
        $G.DrawLine($arrowPen, ($badgeX - $badgeR*0.5), ($badgeY - $badgeR*0.25), ($badgeX + $badgeR*0.5), ($badgeY - $badgeR*0.25))
        $G.DrawLine($arrowPen, ($badgeX - $badgeR*0.5), ($badgeY + $badgeR*0.35), ($badgeX + $badgeR*0.5), ($badgeY + $badgeR*0.35))
        $arrowPen.Dispose()
        $badgeBrush.Dispose()
    }

    $whitePen.Dispose()
    $whiteBrush.Dispose()
    $brush.Dispose()
    $path.Dispose()
}

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

    Draw-MailBridgeGlyph -G $g -Width $Width -Height $Height

    $g.Dispose()
    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Get-IcoBmpData {
    param([System.Drawing.Bitmap]$Bmp)

    $w = $Bmp.Width
    $h = $Bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Bmp.UnlockBits($data)

    $rowBytes = $w * 4
    $xor = New-Object byte[] ($rowBytes * $h)
    for ($y = 0; $y -lt $h; $y++) {
        [Array]::Copy($pixels, ($h - 1 - $y) * $stride, $xor, $y * $rowBytes, $rowBytes)
    }

    $maskRow = [int][Math]::Ceiling($w / 32.0) * 4
    $mask = New-Object byte[] ($maskRow * $h)

    $header = New-Object byte[] 40
    [Array]::Copy([System.BitConverter]::GetBytes([uint32]40), 0, $header, 0, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int32]$w), 0, $header, 4, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int32]($h * 2)), 0, $header, 8, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([uint16]1), 0, $header, 12, 2)
    [Array]::Copy([System.BitConverter]::GetBytes([uint16]32), 0, $header, 14, 2)
    [Array]::Copy([System.BitConverter]::GetBytes([uint32]($xor.Length + $mask.Length)), 0, $header, 20, 4)

    $ms = New-Object System.IO.MemoryStream
    $ms.Write($header, 0, 40)
    $ms.Write($xor, 0, $xor.Length)
    $ms.Write($mask, 0, $mask.Length)
    $ms.ToArray()
}

function New-AppIco {
    param([string]$OutPath)

    $sizes = @(16, 24, 32, 48, 64, 256)
    $images = New-Object 'System.Collections.Generic.List[byte[]]'
    $entries = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        Draw-MailBridgeGlyph -G $g -Width $s -Height $s
        $g.Dispose()

        if ($s -ge 256) {
            $pngStream = New-Object System.IO.MemoryStream
            $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $null = $images.Add($pngStream.ToArray())
        }
        else {
            $data = Get-IcoBmpData -Bmp $bmp
            $null = $images.Add($data)
        }

        $bmp.Dispose()

        $entry = New-Object byte[] 16
        if ($s -ge 256) { $entry[0] = 0; $entry[1] = 0 } else { $entry[0] = $s; $entry[1] = $s }
        $entry[2] = 0
        $entry[3] = 0
        [Array]::Copy([System.BitConverter]::GetBytes([uint16]1), 0, $entry, 4, 2)
        [Array]::Copy([System.BitConverter]::GetBytes([uint16]32), 0, $entry, 6, 2)
        [Array]::Copy([System.BitConverter]::GetBytes([uint32]$images[$images.Count - 1].Length), 0, $entry, 8, 4)
        $null = $entries.Add($entry)
    }

    $ico = New-Object 'System.Collections.Generic.List[byte]'
    $header = New-Object byte[] 6
    $header[2] = 1
    [Array]::Copy([System.BitConverter]::GetBytes([uint16]$sizes.Count), 0, $header, 4, 2)
    $ico.AddRange($header)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        [Array]::Copy([System.BitConverter]::GetBytes([uint32]$offset), 0, $entries[$i], 12, 4)
        $ico.AddRange($entries[$i])
        $offset += $images[$i].Length
    }

    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $ico.AddRange($images[$i])
    }

    [System.IO.File]::WriteAllBytes($OutPath, $ico.ToArray())
}

New-MailBridgeIcon -Width 44  -Height 44  -OutPath (Join-Path $assetsDir "Square44x44Logo.png")
New-MailBridgeIcon -Width 150 -Height 150 -OutPath (Join-Path $assetsDir "Square150x150Logo.png")
New-MailBridgeIcon -Width 71  -Height 71  -OutPath (Join-Path $assetsDir "Square71x71Logo.png")
New-MailBridgeIcon -Width 310 -Height 150 -OutPath (Join-Path $assetsDir "Wide310x150Logo.png")
New-MailBridgeIcon -Width 50  -Height 50  -OutPath (Join-Path $assetsDir "StoreLogo.png")
New-MailBridgeIcon -Width 24  -Height 24  -OutPath (Join-Path $assetsDir "LockScreenLogo.png")
New-MailBridgeIcon -Width 620 -Height 300 -OutPath (Join-Path $assetsDir "SplashScreen.png")
New-MailBridgeIcon -Width 256 -Height 256 -OutPath (Join-Path $assetsDir "AppIcon.png")

New-AppIco -OutPath (Join-Path $assetsDir "app.ico")

Write-Output "Icons written to $assetsDir (incl. app.ico)"
