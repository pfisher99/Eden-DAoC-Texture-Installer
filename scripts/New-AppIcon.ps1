param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\SqeaksDaocTextureInstaller\Assets\SqeaksDaocTextures.ico')
)

Add-Type -AssemblyName System.Drawing

$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in 16, 32, 48, 256) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(1, [Math]::Round($size * 0.06))
    $diameter = $size - ($inset * 2)
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 210, 168, 92))
    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 140, 90, 25), [Math]::Max(1, $size / 32))
    $radius = [Math]::Max(2, [Math]::Round($diameter * 0.22))
    $arc = $radius * 2
    $crest = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $crest.AddArc($inset, $inset, $arc, $arc, 180, 90)
    $crest.AddArc($inset + $diameter - $arc, $inset, $arc, $arc, 270, 90)
    $crest.AddArc($inset + $diameter - $arc, $inset + $diameter - $arc, $arc, $arc, 0, 90)
    $crest.AddArc($inset, $inset + $diameter - $arc, $arc, $arc, 90, 90)
    $crest.CloseFigure()
    $graphics.FillPath($gold, $crest)
    $graphics.DrawPath($border, $crest)

    $fontSize = [Math]::Max(8, $size * 0.58)
    $font = [System.Drawing.Font]::new('Cambria', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 33, 23, 11))
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString('S', $font, $ink, [System.Drawing.RectangleF]::new(0, -($size * 0.02), $size, $size), $format)

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())
    $stream.Dispose()
    $format.Dispose()
    $ink.Dispose()
    $font.Dispose()
    $border.Dispose()
    $gold.Dispose()
    $crest.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$directory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$output = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($output)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
for ($index = 0; $index -lt $frames.Count; $index++) {
    $size = @(16, 32, 48, 256)[$index]
    $dimension = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()
