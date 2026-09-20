#Requires -Version 7
<#
.SYNOPSIS
    Builds the desktop app icon from the web favicon, so both wear the same face.

.DESCRIPTION
    Regenerate this whenever src/SemperSounds.Web/wwwroot/favicon.png changes.

    The entries are PNG-compressed rather than the older BMP form. Windows has accepted that
    since Vista, it is what keeps a 256px entry from costing 256 KB on its own, and it avoids
    hand-rolling the AND mask that the BMP form still requires even for 32-bit images.
#>
param(
    [string]$Source = "src/SemperSounds.Web/wwwroot/favicon.png",
    [string]$Out    = "src/SemperSounds.Desktop/Assets/app.ico"
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
Add-Type -AssemblyName System.Drawing

# 16 and 20 are the tray and title bar; 256 is what Explorer shows at large icon sizes.
$sizes = 16, 20, 24, 32, 48, 64, 128, 256

# Not $source: PowerShell variable names are case-insensitive, so that is the $Source parameter.
$original = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$images = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($original, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}
$original.Dispose()

# Likewise not $out, which is the $Out parameter.
$icoStream = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($icoStream)

$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)   # ICONDIR

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    # 256 is written as 0: the field is a single byte and 256 does not fit in it.
    $dim = if ($img.Size -eq 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Bytes.Length)
    $w.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }

$w.Flush()

# Taken before the writer is disposed, which disposes the stream under it and would leave the
# report below describing a closed stream rather than the file just written.
$bytes = $icoStream.ToArray()
$w.Dispose()

[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $Out), $bytes)

Write-Output "Wrote $Out ($($images.Count) sizes, $([math]::Round($bytes.Length / 1kb, 1)) KB)"
