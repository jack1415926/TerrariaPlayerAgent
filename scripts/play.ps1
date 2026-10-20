param([string]$TmlPath = 'F:\SteamLibrary\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$profile = Join-Path $projectRoot '.local\tModLoader'
$modFile = Join-Path $profile 'Mods\TerrariaPlayerAgent.tmod'
if (-not (Test-Path -LiteralPath $modFile)) { throw 'Build first: ./scripts/build.ps1' }
# Only this project's isolated profile is configured. Existing mod lists and saves are untouched.
Set-Content -LiteralPath (Join-Path $profile 'Mods\enabled.json') -Value '["TerrariaPlayerAgent"]' -Encoding utf8
Start-Process -FilePath (Join-Path $TmlPath 'dotnet\dotnet.exe') -WorkingDirectory $TmlPath `
    -ArgumentList @('tModLoader.dll', '-tmlsavedirectory', ('"' + $profile + '"'), '-tpatest') -WindowStyle Hidden
