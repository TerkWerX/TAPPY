[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Tappy.App\Assets\Branding\tappy-hand-t.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Tappy.App\Assets\Icons\tappy.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = [System.IO.Path]::GetFullPath($Source)
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationPath)

if (-not [System.IO.File]::Exists($sourcePath)) {
    throw "Icon source does not exist: $sourcePath"
}

[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

                $padding = [Math]::Max(1, [int][Math]::Round($size * 0.04))
                $available = $size - (2 * $padding)
                $scale = [Math]::Min($available / $sourceImage.Width, $available / $sourceImage.Height)
                $width = [Math]::Max(1, [int][Math]::Round($sourceImage.Width * $scale))
                $height = [Math]::Max(1, [int][Math]::Round($sourceImage.Height * $scale))
                $left = [int][Math]::Floor(($size - $width) / 2)
                $top = [int][Math]::Floor(($size - $height) / 2)
                $rectangle = [System.Drawing.Rectangle]::new($left, $top, $width, $height)
                $graphics.DrawImage($sourceImage, $rectangle)
            }
            finally {
                $graphics.Dispose()
            }

            $memory = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($memory.ToArray())
            }
            finally {
                $memory.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$temporaryPath = "$destinationPath.new"
$stream = [System.IO.File]::Open(
    $temporaryPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = [System.IO.BinaryWriter]::new($stream)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
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

    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

[System.IO.File]::Move($temporaryPath, $destinationPath, $true)
Write-Output "Created $destinationPath from $sourcePath with sizes $($sizes -join ', ')."
