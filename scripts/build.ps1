param([string]$TmlPath = 'F:\SteamLibrary\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath (Join-Path $TmlPath 'tMLMod.targets'))) {
    throw 'tModLoader not found. Supply -TmlPath with your installation directory.'
}
Push-Location $projectRoot
try {
    dotnet build (Join-Path $projectRoot 'TerrariaPlayerAgent.csproj') "-p:TmlInstallPath=$TmlPath" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
