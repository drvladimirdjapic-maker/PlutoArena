<#
  Builds the Pluto Arena installer.

  1. dotnet publish  -> self-contained PlutoArena.exe (friends do not need .NET)
  2. copies the Pluto Remastered bridge (native\bin + native\scripts),
     the difficulty DLLs and the arena maps into installer\payload
  3. runs Inno Setup (ISCC.exe)       -> installer\output\PlutoArena-2.0-Setup.exe

  NOT included (friends get them themselves): StarCraft: Remastered and
  Pluto AI (pluto.dll + pluto\pluto_infer.exe + pluto\pluto_weights.bin).

  Usage (in the PlutoArena folder):
      powershell -ExecutionPolicy Bypass -File .\installer\publish.ps1
  Options:
      -NoMaps      do not include the maps
      -NoModes     do not include the difficulty DLLs
      -ModesFolder / -MapsFolder / -NativeFolder / -BridgeSource   other source folders
      -Iscc "C:\...\ISCC.exe"     Inno Setup compiler, if it is not found automatically
#>
param(
  [switch]$NoMaps,
  [switch]$NoModes,
  [string]$ModesFolder = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PlutoDifficultyPack\modes'),
  [string]$MapsFolder = (Join-Path ${env:ProgramFiles(x86)} 'StarCraft\Maps\000_PlutoArena'),
  [string]$NativeFolder = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PlutoRemasteredControlCenter\native'),
  [string]$BridgeSource = (Join-Path $PSScriptRoot '..\bridge-src'),
  [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'

$installer = $PSScriptRoot
$project = Split-Path $installer -Parent
$payload = Join-Path $installer 'payload'

Write-Host '== Cleaning payload' -ForegroundColor Cyan
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null

Write-Host '== Publishing Pluto Arena (self-contained, win-x64)' -ForegroundColor Cyan
dotnet publish (Join-Path $project 'PlutoArena.csproj') `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -o (Join-Path $payload 'app')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Difficulty DLLs: Original (unchanged bridge) + tuned bridge (reaction delay + APM limit)
$original = Join-Path $ModesFolder 'pluto-scr-original.dll'
$tuned = Join-Path $ModesFolder 'pluto-scr-tuned.dll'
foreach ($f in @($original, $tuned)) {
  if (-not (Test-Path $f)) { throw "Missing $f (build the bridge with scripts\build-arena.ps1, or use -ModesFolder)" }
}

if (-not $NoModes) {
  Write-Host "== Copying difficulty DLLs from $ModesFolder" -ForegroundColor Cyan
  $dest = New-Item -ItemType Directory -Path (Join-Path $payload 'modes')
  Copy-Item $original, $tuned $dest
}

Write-Host "== Copying Pluto Remastered bridge from $NativeFolder" -ForegroundColor Cyan
foreach ($f in @('bin\scr-loader.exe', 'bin\build-capabilities.txt', 'scripts\start-pluto.ps1')) {
  if (-not (Test-Path (Join-Path $NativeFolder $f))) { throw "Missing $(Join-Path $NativeFolder $f) (use -NativeFolder)" }
}
$nativeOut = Join-Path $payload 'native'
$binOut = New-Item -ItemType Directory -Path (Join-Path $nativeOut 'bin') -Force
$scriptsOut = New-Item -ItemType Directory -Path (Join-Path $nativeOut 'scripts') -Force
Copy-Item (Join-Path $NativeFolder 'bin\scr-loader.exe') $binOut
Copy-Item (Join-Path $NativeFolder 'bin\build-capabilities.txt') $binOut
# Pluto Arena copies the selected difficulty DLL over bin\pluto-scr.dll before
# every game; ship the tuned one so the folder is complete right away.
Copy-Item $tuned (Join-Path $binOut 'pluto-scr.dll')
Copy-Item (Join-Path $NativeFolder 'scripts\start-pluto.ps1') $scriptsOut

# Licenses and source of the changes (BWAPI is LGPL v3, MinHook is BSD)
$licOut = New-Item -ItemType Directory -Path (Join-Path $nativeOut 'licenses') -Force
if (Test-Path $BridgeSource) {
  Write-Host "== Copying licenses and modified source from $BridgeSource" -ForegroundColor Cyan
  $third = Join-Path $BridgeSource 'THIRD_PARTY.md'
  if (Test-Path $third) { Copy-Item $third $licOut }
  $bwapi = Join-Path $BridgeSource 'vendor\bwapi'
  if (Test-Path $bwapi) {
    $bwOut = New-Item -ItemType Directory -Path (Join-Path $licOut 'bwapi') -Force
    Get-ChildItem $bwapi -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' } | Copy-Item -Destination $bwOut
  }
  $minhook = Join-Path $BridgeSource 'vendor\minhook'
  if (Test-Path $minhook) {
    $mhOut = New-Item -ItemType Directory -Path (Join-Path $licOut 'minhook') -Force
    Get-ChildItem $minhook -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|THIRD)' } | Copy-Item -Destination $mhOut
  }
  $srcOut = New-Item -ItemType Directory -Path (Join-Path $nativeOut 'source') -Force
  foreach ($f in @('src\legacy_view.cpp', 'src\legacy_view.h', 'src\pluto_bindings.h', 'src\file_hash.h', 'src\bridge.cpp', 'scripts\build-arena.ps1')) {
    $p = Join-Path $BridgeSource $f
    if (Test-Path $p) { Copy-Item $p $srcOut }
  }
  @(
    'Pluto Remastered bridge',
    'Original source: https://github.com/lpflhh/Pluto-AI-Starcraft-Remaster',
    '',
    'Pluto Arena changes:',
    ' - src\legacy_view.cpp, src\legacy_view.h: difficulty (reaction delay + APM',
    '   limit, read from PLUTO_REACTION_MS / PLUTO_APM) and the binding table choice',
    ' - src\pluto_bindings.h, src\file_hash.h, src\bridge.cpp: Pluto v2.0 support',
    ' - scripts\start-pluto.ps1 (in native\scripts): accepts Pluto cog2026 and v2.0',
    'The modified files are in this folder. Copy them over the original',
    'source and build with scripts\build.ps1 to rebuild bin\pluto-scr.dll.'
  ) | Set-Content -Path (Join-Path $srcOut 'README.txt') -Encoding ASCII
} else {
  Write-Warning "Bridge source not found ($BridgeSource) - licenses are not included. Use -BridgeSource."
}

if (-not $NoMaps) {
  Write-Host "== Copying maps from $MapsFolder" -ForegroundColor Cyan
  if (-not (Test-Path $MapsFolder)) { throw "Maps folder not found: $MapsFolder (use -MapsFolder or -NoMaps)" }
  $dest = New-Item -ItemType Directory -Path (Join-Path $payload 'maps')
  Get-ChildItem $MapsFolder -File | Where-Object { $_.Extension -in '.scx', '.scm' } | Copy-Item -Destination $dest
}

Write-Host '== Building installer' -ForegroundColor Cyan
function Find-Iscc {
  if ($Iscc -and (Test-Path $Iscc)) { return $Iscc }

  # 1. Inno Setup writes its folder into the uninstall registry key.
  $keys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1'
  )
  foreach ($k in $keys) {
    $loc = (Get-ItemProperty -Path $k -ErrorAction SilentlyContinue).InstallLocation
    if ($loc -and (Test-Path (Join-Path $loc 'ISCC.exe'))) { return (Join-Path $loc 'ISCC.exe') }
  }

  # 2. Usual folders.
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 5\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 5\ISCC.exe')
  )
  foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

  # 3. On the PATH.
  $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  return $null
}

$iscc = Find-Iscc

if (-not $iscc) {
  throw 'Inno Setup (ISCC.exe) not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php, or pass its path: -Iscc "C:\...\ISCC.exe"'
}

Write-Host "Using $iscc"
& $iscc (Join-Path $installer 'PlutoArena.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

Write-Host ''
Write-Host "Done: $(Join-Path $installer 'output')" -ForegroundColor Green
