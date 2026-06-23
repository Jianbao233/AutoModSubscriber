param(
    [string]$GodotExe = "K:\杀戮尖塔mod制作\Godot_v4.5.1\Godot_v4.5.1\Godot_v4.5.1-stable_mono_win64.exe",
    [string]$Config = "Debug"
)
$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ModId = "AutoModSubscriber"

$GameModsDir    = "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\$ModId"
$GameModsDirAlt = "C:\Users\Administrator\AppData\Roaming\SlayTheSpire2\mods\$ModId"

Write-Host "=== $ModId Build ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"
Write-Host "Config:  $Config"

if (Test-Path "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods") {
    $TargetDir = $GameModsDir
} else {
    $TargetDir = $GameModsDirAlt
}
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
Write-Host "Target:  $TargetDir" -ForegroundColor Cyan

Write-Host "[1/3] dotnet build..." -ForegroundColor Yellow
dotnet build -c $Config --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
Write-Host "  dotnet build OK" -ForegroundColor Green

$DllPath = "$ProjectRoot\.godot\mono\temp\bin\$Config\$ModId.dll"
if (-not (Test-Path $DllPath)) {
    throw "DLL not found at $DllPath"
}
Copy-Item -Path $DllPath -Destination "$TargetDir\$ModId.dll" -Force
Write-Host "[2/3] Copied $ModId.dll OK" -ForegroundColor Green

if (Test-Path "$ProjectRoot\mod_manifest.json") {
    Copy-Item -Path "$ProjectRoot\mod_manifest.json" -Destination "$TargetDir\mod_manifest.json" -Force
    python -c @"
import json
src = r'$TargetDir\mod_manifest.json'
with open(src, 'r', encoding='utf-8') as f:
    data = json.load(f)
with open(src, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
"@
    Write-Host "[3/3] Copied + validated mod_manifest.json OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build complete. Target: $TargetDir" -ForegroundColor Green