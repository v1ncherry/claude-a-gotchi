# Builds the release files into dist/:
#   claude-a-gotchi-<version>-setup.exe   (Inno Setup installer)
#   claude-a-gotchi-<version>-win-x64.zip (portable)
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File tools/build.ps1 [-Version 1.2.3]
# Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\claude-a-gotchi\claude-a-gotchi.csproj"
if (-not $Version) { $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$Version = $Version.TrimStart("v")
Write-Host "Building claude-a-gotchi $Version"

$publish = Join-Path $root "build\publish"
$dist = Join-Path $root "dist"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

# Self-contained: .NET comes with the app, nothing else needs to be installed.
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Every costume and language file must be valid before anything is shipped.
& (Join-Path $publish "claude-a-gotchi.exe") --check $root | Out-Host
if ($LASTEXITCODE -ne 0) { throw "costume or language check failed" }

$zip = Join-Path $dist "claude-a-gotchi-$Version-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$publish" (Join-Path $root "installer\claude-a-gotchi.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-ChildItem $dist | Where-Object { $_.Name -like "*$Version*" } | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
