param([string]$TmlPath = 'F:\SteamLibrary\steamapps\common\tModLoader', [switch]$MultiplayerBridge, [switch]$MultiplayerAgent, [switch]$MultiplayerTestEquip)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$profile = Join-Path $projectRoot '.local\tModLoader'
$modFile = Join-Path $profile 'Mods\TerrariaPlayerAgent.tmod'
if (-not (Test-Path -LiteralPath $modFile)) { throw 'Build first: ./scripts/build.ps1' }
# Only this project's isolated profile is configured. Existing mod lists and saves are untouched.
Set-Content -LiteralPath (Join-Path $profile 'Mods\enabled.json') -Value '["TerrariaPlayerAgent","CheatSheet","HEROsMod"]' -Encoding utf8
$launchOptions = @{}
$environment = @{}
# Host & Play forwards neither -tpatest nor custom flags, but its server inherits the environment.
if ($MultiplayerBridge -or $MultiplayerAgent) { $environment.TPA_MP_READONLY_BRIDGE = '1' }
if ($MultiplayerAgent) { $environment.TPA_MP_AGENT = '1' }
# Test equipment is a separate opt-in; normal -MultiplayerAgent sessions stay fixture-free.
if ($MultiplayerTestEquip) { $environment.TPA_MP_TEST_EQUIP = '1' }
if ($environment.Count -gt 0) { $launchOptions.Environment = $environment }
Start-Process @launchOptions -FilePath (Join-Path $TmlPath 'dotnet\dotnet.exe') -WorkingDirectory $TmlPath `
    -ArgumentList @('tModLoader.dll', '-tmlsavedirectory', ('"' + $profile + '"'), '-tpatest') -WindowStyle Hidden
