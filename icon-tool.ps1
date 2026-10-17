# Generates cava.ico from the JPG in Pictures (multi-size ICO, GDI+, no NuGet)
$ErrorActionPreference = 'Stop'
$src = 'C:\Users\0121\Pictures\08b752caab4fca269553b4403026abc6.jpg'
$out = Join-Path $PSScriptRoot 'cava.ico'
$log = Join-Path $PSScriptRoot 'icon-result.txt'
$lines = @()
try {
  Add-Type -AssemblyName System.Drawing
  $img = [System.Drawing.Image]::FromFile($src)
  $side = [Math]::Min($img.Width, $img.Height)
  $cx = [int](($img.Width - $side) / 2)
  $cy = [int](($img.Height - $side) / 2)
  $crop = New-Object System.Drawing.Rectangle ($cx), ($cy), ($side), ($side)
  $entries = @()
  foreach ($s in @(16, 32, 48, 64, 128, 256)) {
    $bmp = New-Object System.Drawing.Bitmap ([int]$s), ([int]$s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $dst = New-Object System.Drawing.Rectangle (0), (0), ($s), ($s)
    $g.DrawImage($img, $dst, $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    if ($s -ge 64) {
      $ms = New-Object System.IO.MemoryStream
      $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
      $entries += [pscustomobject]@{ W = $s; H = $s; Data = $ms.ToArray() }
      $ms.Dispose()
    } else {
      $rect = New-Object System.Drawing.Rectangle (0), (0), ($s), ($s)
      $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
      $raw = New-Object byte[] ([int]($s * $s * 4))
      [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $raw, 0, $raw.Length)
      $stride = [int]$bd.Stride
      $bmp.UnlockBits($bd)
      $px = New-Object byte[] ($raw.Length)
      for ($y = 0; $y -lt $s; $y++) {
        [Array]::Copy($raw, (($s - 1 - $y) * $stride), $px, ($y * $stride), $stride)
      }
      $maskStride = [int]([Math]::Ceiling([Math]::Ceiling($s / 8.0) / 4.0) * 4)
      $mask = New-Object byte[] ($maskStride * $s)
      $ms = New-Object System.IO.MemoryStream
      $bw = New-Object System.IO.BinaryWriter ($ms)
      [void]$bw.Write([uint32]40)
      [void]$bw.Write([int32]$s)
      [void]$bw.Write([int32]($s * 2))
      [void]$bw.Write([uint16]1)
      [void]$bw.Write([uint16]32)
      [void]$bw.Write([uint32]0)
      [void]$bw.Write([uint32]($px.Length + $mask.Length))
      [void]$bw.Write([int32]0)
      [void]$bw.Write([int32]0)
      [void]$bw.Write([uint32]0)
      [void]$bw.Write([uint32]0)
      [void]$bw.Write($px)
      [void]$bw.Write($mask)
      $bw.Flush()
      $entries += [pscustomobject]@{ W = $s; H = $s; Data = $ms.ToArray() }
      $bw.Dispose()
    }
    $bmp.Dispose()
  }
  $img.Dispose()
  $fs = [System.IO.File]::Create($out)
  $bw = New-Object System.IO.BinaryWriter ($fs)
  [void]$bw.Write([uint16]0)
  [void]$bw.Write([uint16]1)
  [void]$bw.Write([uint16]$entries.Count)
  $off = 6 + 16 * $entries.Count
  foreach ($e in $entries) {
    $wb = if ($e.W -ge 256) { [byte]0 } else { [byte]$e.W }
    $hb = if ($e.H -ge 256) { [byte]0 } else { [byte]$e.H }
    [void]$bw.Write($wb)
    [void]$bw.Write($hb)
    [void]$bw.Write([byte]0)
    [void]$bw.Write([byte]0)
    [void]$bw.Write([uint16]1)
    [void]$bw.Write([uint16]32)
    [void]$bw.Write([uint32]$e.Data.Length)
    [void]$bw.Write([uint32]$off)
    $off += $e.Data.Length
  }
  foreach ($e in $entries) { [void]$bw.Write($e.Data) }
  $bw.Flush()
  $bw.Dispose()
  $lines += ("OK ico {0} bytes, {1} entries" -f (Get-Item $out).Length, $entries.Count)
} catch {
  $lines += ("FAIL " + $_.Exception.ToString())
}
$lines | Set-Content $log