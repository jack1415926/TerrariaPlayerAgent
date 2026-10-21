param([string]$Goal, [string]$GoalFile, [int]$MaxTurns = 0, [switch]$CheckApi)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$environmentFile = Join-Path $projectRoot '.env'
if (Test-Path -LiteralPath $environmentFile) {
    foreach ($line in Get-Content -LiteralPath $environmentFile) {
        if ($line -match '^\s*(TPA_[A-Z0-9_]+)=(.*)$') {
            $name, $value = $Matches[1], $Matches[2].Trim()
            if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) {
                [Environment]::SetEnvironmentVariable($name, $value, 'Process')
            }
        }
    }
}
$env:PYTHONIOENCODING = 'utf-8'
$agentArguments = @('-u', (Join-Path $projectRoot 'agent/run.py'))
if ($CheckApi) { $agentArguments += '--check-api' }
if ($Goal) { $agentArguments += @('--goal', $Goal) }
if ($GoalFile) { $agentArguments += @('--goal-file', $GoalFile) }
if ($MaxTurns -gt 0) { $agentArguments += @('--max-turns', $MaxTurns) }
& python @agentArguments
if ($LASTEXITCODE -ne 0) { throw "Agent exited with code $LASTEXITCODE. See the local report." }
