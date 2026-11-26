# Renders every picture and GIF used in the README into docs/images.
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File tools/make-media.ps1
# Needs the .NET 8 SDK and Python with Pillow (pip install pillow).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "docs\images"
$build = Join-Path $root "build\media"

dotnet build (Join-Path $root "src\claude-a-gotchi") -c Release -o $build --nologo -v q
$exe = Join-Path $build "claude-a-gotchi.exe"

# Costume rows, emotions, beds, limit plate (English names; German ones go to docs/images/de)
& $exe --gallery $out en | Out-Host
& $exe --gallery (Join-Path $out "de") de | Out-Host

# Window screenshots with example values
foreach ($lang in "en", "de") {
    $dir = if ($lang -eq "en") { $out } else { Join-Path $out "de" }
    foreach ($w in "panel", "look", "bed", "settings", "welcome") {
        & $exe --ui-test $w (Join-Path $dir "ui-$w.png") $lang | Out-Host
    }
}

# Animations
$frames = Join-Path $build "frames"
& $exe --frames $frames | Out-Host
python -I (Join-Path $PSScriptRoot "frames-to-gif.py") $frames $out 1
