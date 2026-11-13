$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$projectRoot = Split-Path $PSScriptRoot -Parent
# Reuse the already-running multiplayer host; this driver never calls the model itself.
& python -B -u (Join-Path $projectRoot 'agent/acceptance.py')
if ($LASTEXITCODE -ne 0) { throw "Multiplayer goal acceptance failed: $LASTEXITCODE. See its report and server log." }
