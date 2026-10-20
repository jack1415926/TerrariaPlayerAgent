param([int]$FocusDelaySeconds = 3)
$ErrorActionPreference = 'Stop'
$invoke = Join-Path $PSScriptRoot 'invoke-test.ps1'
$results = [Collections.Generic.List[object]]::new()
function Assert-Demo([string]$Name, [bool]$Condition, $Evidence) {
    $results.Add(@{test=$Name;passed=$Condition;evidence=$Evidence})
    if (-not $Condition) { throw "FAIL: $Name" }
    Write-Host "PASS: $Name"
}
try {
    if ($FocusDelaySeconds -gt 0) {
        Write-Host "Switch to the test game within $FocusDelaySeconds seconds."
        Start-Sleep -Seconds $FocusDelaySeconds
    }
    $ready = & $invoke -Command status
    Assert-Demo 'single-player world is running' (-not $ready.state.gameMenu -and $ready.state.netMode -eq 0) $ready.state
    Assert-Demo 'game is focused and simulation is running' ($ready.state.input.focused -and -not $ready.state.paused) $ready.state.input
    & $invoke -Command '/tpa remove' | Out-Null
    $spawn = & $invoke -Command '/tpa spawn'
    Assert-Demo 'independent Player exists' ($null -ne $spawn.state.actor -and $spawn.state.actor.slot -ne $spawn.state.player.slot) $spawn.state
    $again = & $invoke -Command '/tpa spawn'
    Assert-Demo 'duplicate spawn preserves actor' ($again.reply -like 'Demo already exists*' -and $again.state.actor.slot -eq $spawn.state.actor.slot) $again.reply
    Start-Sleep -Milliseconds 500
    $before = & $invoke -Command status
    & $invoke -Command '/tpa right 0.3' | Out-Null
    Start-Sleep -Milliseconds 700
    $after = & $invoke -Command status
    Assert-Demo 'native update runs once per tick' ($after.state.actor.updatesPerTick -eq 1) $after.state.actor
    Assert-Demo 'right movement and timeout' ($after.state.actor.x -gt $before.state.actor.x + 2 -and $after.state.actor.moveTicks -eq 0) @{before=$before.state.actor;after=$after.state.actor}
    & $invoke -Command '/tpa right 10' | Out-Null
    Start-Sleep -Milliseconds 200
    $stopped = & $invoke -Command '/tpa stop'
    Assert-Demo 'stop clears input before timeout' ($stopped.state.actor.moveTicks -eq 0 -and -not $stopped.state.actor.right -and -not $stopped.state.actor.left) $stopped.state.actor
    Start-Sleep -Milliseconds 300
    $ground = & $invoke -Command status
    Assert-Demo 'jump starts on ground' ($ground.state.actor.velocityY -eq 0) $ground.state.actor
    & $invoke -Command '/tpa jump' | Out-Null
    Start-Sleep -Milliseconds 100
    $jumped = & $invoke -Command status
    Assert-Demo 'native jump lifts the actor' ($jumped.state.actor.y -lt $ground.state.actor.y - 2) @{before=$ground.state.actor;after=$jumped.state.actor}
    & $invoke -Command '/tpa remove' | Out-Null
    $removed = & $invoke -Command '/tpa remove'
    Assert-Demo 'repeated removal leaves no actor' ($null -eq $removed.state.actor) $removed.state
} catch {
    $results.Add(@{test='execution';passed=$false;error=$_.Exception.Message})
    throw
} finally {
    # A failure must not leave a ten-second movement running.
    try { & $invoke -Command '/tpa remove' | Out-Null } catch { Write-Warning $_ }
    $directory = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\test-results'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $path = Join-Path $directory ('smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Host "Report: $path"
}
