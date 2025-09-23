# build-plugin.ps1
$ErrorActionPreference = 'Stop'

# Known-good paths
$env:BEPINEX_DIR  = "C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\BepInEx"
$env:GAME_MANAGED = "C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\EtG_Data\Managed"

Write-Host "BEPINEX_DIR  = $env:BEPINEX_DIR"
Write-Host "GAME_MANAGED = $env:GAME_MANAGED"

# Sanity checks
if (-not (Test-Path "$env:BEPINEX_DIR\core\BepInEx.dll")) { throw "Missing BepInEx.dll" }
if (-not (Test-Path "$env:BEPINEX_DIR\core\0Harmony.dll")) { throw "Missing 0Harmony.dll" }
if (-not (Test-Path "$env:GAME_MANAGED\UnityEngine.dll"))  { throw "Missing UnityEngine.dll" }

# 1) Build dev target with nullable enabled (OK on net9.0)
Write-Host "Building EtG.Plugin (net9.0)..."
dotnet build .\EtG.Plugin\EtG.Plugin.csproj -c Release -f net9.0 /p:Nullable=enable

# 2) Build runtime plugin with nullable disabled (required for C# 7.3 / net35)
Write-Host "Building EtG.Plugin (net35)..."
dotnet build .\EtG.Plugin\EtG.Plugin.csproj -c Release -f net35 `
    /p:BEPINEX_DIR="$env:BEPINEX_DIR" `
    /p:GAME_MANAGED="$env:GAME_MANAGED" `
    /p:Nullable=disable

Write-Host ""
Read-Host "Done. Press Enter to close."
