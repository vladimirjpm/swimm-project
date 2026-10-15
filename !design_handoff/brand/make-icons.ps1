param([string]$Src, [string]$OutDir, [int]$Radius = 222)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$srcBmp = [System.Drawing.Bitmap]::FromFile($Src)
$W = $srcBmp.Width; $H = $srcBmp.Height
$navy = [System.Drawing.Color]::FromArgb(255, 0x02, 0x1A, 0x49)

# Две основы в полном размере:
#  rounded — прозрачные углы (favicon во вкладке браузера);
#  square  — углы залиты фоном логотипа (иконки телефона: iOS/Android скругляют сами).
$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$r = $Radius

# square: исходник + угловые квадраты r×r залиты фоном (логотипа в них нет — только фон и белое поле).
$square = [System.Drawing.Bitmap]::new($W, $H, $fmt)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.DrawImage($srcBmp, 0, 0, $W, $H)
$brush = [System.Drawing.SolidBrush]::new($navy)
foreach ($pt in @(@(0, 0), @(($W - $r), 0), @(0, ($H - $r)), @(($W - $r), ($H - $r)))) { $g.FillRectangle($brush, $pt[0], $pt[1], $r, $r) }
$g.Dispose(); $srcBmp.Dispose()

# rounded: square, вырезанный сглаженным скруглённым прямоугольником — снаружи прозрачно.
$rounded = [System.Drawing.Bitmap]::new($W, $H, $fmt)
$g = [System.Drawing.Graphics]::FromImage($rounded)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$d = 2 * $r
$path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($W - $d, 0, $d, $d, 270, 90)
$path.AddArc($W - $d, $H - $d, $d, $d, 0, 90); $path.AddArc(0, $H - $d, $d, $d, 90, 90); $path.CloseFigure()
$tex = [System.Drawing.TextureBrush]::new($square)
$g.FillPath($tex, $path)
$g.Dispose(); $tex.Dispose(); $path.Dispose()

function Resize([System.Drawing.Bitmap]$bmp, [int]$size) {
  $out = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($out)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $attr = [System.Drawing.Imaging.ImageAttributes]::new()
  $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
  $g.DrawImage($bmp, ([System.Drawing.Rectangle]::new(0, 0, $size, $size)), 0, 0, $bmp.Width, $bmp.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
  $g.Dispose(); return $out
}
function PngBytes([System.Drawing.Bitmap]$bmp) {
  $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return ,$ms.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

# Иконки телефона и манифеста — квадрат на всю площадь.
foreach ($p in @(@{n='apple-touch-icon.png'; s=180}, @{n='logo192.png'; s=192}, @{n='logo512.png'; s=512})) {
  $img = Resize $square $p.s; [IO.File]::WriteAllBytes((Join-Path $OutDir $p.n), (PngBytes $img)); $img.Dispose()
}
# Логотип с прозрачными углами для страниц (шапка, письма, Branding) — 512.
$img = Resize $rounded 512; [IO.File]::WriteAllBytes((Join-Path $OutDir 'swimhub-logo.png'), (PngBytes $img)); $img.Dispose()

# favicon.ico — PNG-записи 16/32/48/64/128/256 с прозрачными углами.
$sizes = 16, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $i = Resize $rounded $s; ,(PngBytes $i); $i.Dispose() }
$ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $pngs[$i].Length
  $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $OutDir 'favicon.ico'), $ms.ToArray())

$rounded.Dispose(); $square.Dispose()
Get-ChildItem $OutDir | ForEach-Object { '{0,-22} {1,8:N0} байт' -f $_.Name, $_.Length }
