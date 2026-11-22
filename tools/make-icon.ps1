# Generates assets\app.ico: a multi-resolution, PNG-compressed icon (16,20,24,32,40,48,64,256).
# The artwork is drawn with System.Drawing by Lakhfana.Theme.RenderLogo (src\UI\Theme.cs), so the
# exe icon, the window icon and the logo shown inside the app are the same design.
#   Large sizes: rounded gradient square, "A" and "ع" with two circular swap arrows.
#   16-24 px:    the same square with a bold ⇄ glyph (letters would be unreadable).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-PreviewDir <folder>]
param([string]$PreviewDir = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$themePath = Join-Path $root 'src\UI\Theme.cs'
$outPath = Join-Path $root 'assets\app.ico'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$source = [System.IO.File]::ReadAllText($themePath, [System.Text.Encoding]::UTF8)
Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies @('System.Drawing', 'System.Windows.Forms')

$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$images = New-Object System.Collections.Generic.List[byte[]]
foreach ($s in $sizes) {
    $bmp = [Lakhfana.Theme]::RenderLogo($s)
    try {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($ms.ToArray())
        $ms.Dispose()
        if ($PreviewDir) {
            New-Item -ItemType Directory -Force $PreviewDir | Out-Null
            $bmp.Save((Join-Path $PreviewDir ("icon-{0}.png" -f $s)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
    } finally { $bmp.Dispose() }
}

# ICONDIR + ICONDIRENTRY[] + PNG blobs
New-Item -ItemType Directory -Force (Split-Path -Parent $outPath) | Out-Null
$fs = [System.IO.File]::Create($outPath)
$w = New-Object System.IO.BinaryWriter $fs
try {
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
} finally { $w.Close() }

Write-Host ("Wrote {0} ({1} sizes, {2:N0} bytes)" -f $outPath, $sizes.Count, (Get-Item $outPath).Length)
