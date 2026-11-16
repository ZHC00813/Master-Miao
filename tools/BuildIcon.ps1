param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$sourceImage = [System.Drawing.Image]::FromFile($Source)
$frames = New-Object System.Collections.Generic.List[byte[]]
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            }
            finally { $graphics.Dispose() }

            $stream = New-Object System.IO.MemoryStream
            try {
                # Icon.ToBitmap in the .NET Framework UI does not reliably
                # decode PNG-backed icon frames. Use a 32-bit DIB with alpha
                # and a DWORD-aligned AND mask for every size instead.
                $dib = New-Object System.IO.BinaryWriter($stream)
                $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                $dib.Write([UInt32]40)
                $dib.Write([Int32]$size)
                $dib.Write([Int32]($size * 2))
                $dib.Write([UInt16]1)
                $dib.Write([UInt16]32)
                $dib.Write([UInt32]0)
                $dib.Write([UInt32]($size * $size * 4))
                foreach ($unused in 1..4) { $dib.Write([UInt32]0) }
                for ($y = $size - 1; $y -ge 0; $y--) {
                    for ($x = 0; $x -lt $size; $x++) {
                        $pixel = $bitmap.GetPixel($x, $y)
                        $dib.Write([Byte]$pixel.B); $dib.Write([Byte]$pixel.G)
                        $dib.Write([Byte]$pixel.R); $dib.Write([Byte]$pixel.A)
                    }
                }
                for ($y = $size - 1; $y -ge 0; $y--) {
                    $mask = New-Object byte[] $maskStride
                    for ($x = 0; $x -lt $size; $x++) {
                        if ($bitmap.GetPixel($x, $y).A -eq 0) {
                            $index = [int][Math]::Floor($x / 8.0)
                            $mask[$index] = $mask[$index] -bor (128 -shr ($x % 8))
                        }
                    }
                    $dib.Write($mask)
                }
                $dib.Flush()
                $frames.Add($stream.ToArray())
            }
            finally { $stream.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }

$destinationDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Destination))
[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
$file = [System.IO.File]::Create($Destination)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
