Param(
  [string]$BepInExDir = "C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\BepInEx",
  [string]$ManagedDir = "C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\EtG_Data\Managed",
  [switch]$SkipNet9,
  [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

function Say($text, $color = 'White') { Write-Host $text -ForegroundColor $color }

$sw = [Diagnostics.Stopwatch]::StartNew()

try {
  Say "Starting build" "Cyan"
  Say "BEPINEX_DIR  = $BepInExDir"
  Say "GAME_MANAGED = $ManagedDir"

  # Sanity checks
  Say "Running sanity checks..."
  $req = @(
    (Join-Path $BepInExDir "core\BepInEx.dll"),
    (Join-Path $BepInExDir "core\0Harmony.dll"),
    (Join-Path $ManagedDir  "UnityEngine.dll")
  )
  foreach ($f in $req) {
    if (Test-Path $f) {
      Say "OK: $f" "Green"
    } else {
      throw "Missing: $f"
    }
  }

  if (-not $NoClean) {
    Say "dotnet clean (Release)..."
    dotnet clean .\EtG.Plugin\EtG.Plugin.csproj -c Release | Out-Null
  }

  if (-not $SkipNet9) {
    Say "Building EtG.Plugin (net9.0)..."
    dotnet build .\EtG.Plugin\EtG.Plugin.csproj -c Release -f net9.0 /p:Nullable=enable
  }

  Say "Building EtG.Plugin (net35)..."
  dotnet build .\EtG.Plugin\EtG.Plugin.csproj -c Release -f net35 `
    /p:BEPINEX_DIR="$BepInExDir" `
    /p:GAME_MANAGED="$ManagedDir" `
    /p:Nullable=disable

  # Verify copy (csproj post-build target copies to BepInEx\plugins\EtG.Plugin\)
  $dst = Join-Path $BepInExDir "plugins\EtG.Plugin\EtG.Plugin.dll"
  if (Test-Path $dst) {
    Say "Deployed plugin -> $dst" "Green"
  } else {
    Say "net35 built. If it wasn't copied, check the CopyToBepInExPlugins target in the .csproj." "Yellow"
  }

  $sw.Stop()
  Say ("Build complete in {0}s" -f [math]::Round($sw.Elapsed.TotalSeconds,2)) "Green"
}
catch {
  $sw.Stop()
  Say ("Build failed: {0}" -f $_.Exception.Message) "Red"
  exit 1
}
