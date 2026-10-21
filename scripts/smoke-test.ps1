param([int]$FocusDelaySeconds = 3, [switch]$Combat)
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
    if ($Combat) {
        # Use native NPCs and item hits; no injected damage or altered NPC stats.
        foreach ($enemy in @('slime', 'zombie', 'eye')) {
            & $invoke -Command '/tpa remove' | Out-Null
            & $invoke -Command '/tpa spawn' | Out-Null
            $weapon = if ($enemy -eq 'eye') { 'wood' } else { 'iron' }
            & $invoke -Command "/tpa equip $weapon" | Out-Null
            & $invoke -Command '/tpa attack right 10' | Out-Null
            $target = & $invoke -Command "/tpa enemy $enemy"
            $npcSlot = $target.state.actor.enemies[-1].slot
            $timer = [Diagnostics.Stopwatch]::StartNew()
            do {
                Start-Sleep -Milliseconds 100
                $fight = & $invoke -Command status
                $hits = @($fight.state.actor.combatEvents | Where-Object { $_.kind -eq 'item-hit' -and $_.npc -eq $npcSlot })
            } while ($hits.Count -eq 0 -and -not $fight.state.actor.dead -and $timer.Elapsed.TotalSeconds -lt 5)
            Assert-Demo "vanilla $weapon sword hits $enemy" ($hits.Count -gt 0 -and
                $hits[0].attacker -eq $fight.state.actor.slot -and
                $hits[0].item -eq $fight.state.actor.item -and
                $hits[0].damage -gt 0 -and $hits[0].targetLifeAfter -lt $target.state.actor.enemies[-1].life) $fight.state.actor
            $stopped = & $invoke -Command '/tpa stop'
            Assert-Demo "stop cancels $enemy attack input" ($stopped.state.actor.attackTicks -eq 0 -and -not $stopped.state.actor.usingItem) $stopped.state.actor
            # Stop permits the current native swing to finish, then no more swings.
            Start-Sleep -Milliseconds 700
            $idle = & $invoke -Command status
            Assert-Demo "swing finishes after stop ($enemy)" ($idle.state.actor.itemAnimation -eq 0) $idle.state.actor
        }
        & $invoke -Command '/tpa remove' | Out-Null
        & $invoke -Command '/tpa spawn' | Out-Null
        $initial = & $invoke -Command status
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            & $invoke -Command '/tpa enemy zombie' | Out-Null
            Start-Sleep -Milliseconds 900
            $victim = & $invoke -Command status
        } while (-not $victim.state.actor.dead -and $timer.Elapsed.TotalSeconds -lt 30)
        Assert-Demo 'native NPC contact hurts AI' ($victim.state.actor.hurtCount -gt 0 -and
            $victim.state.actor.life -lt $initial.state.actor.life) $victim.state.actor
        Assert-Demo 'native lethal NPC damage kills AI' ($victim.state.actor.dead -and
            $victim.state.actor.life -eq 0 -and $victim.state.actor.deathCount -eq 1) $victim.state.actor
        foreach ($command in @('/tpa attack right', '/tpa right', '/tpa jump')) {
            $dead = & $invoke -Command $command
            Assert-Demo "dead actor rejects $command" ($dead.reply -like 'No living demo*' -and $dead.state.actor.attackTicks -eq 0) $dead.reply
        }
        & $invoke -Command '/tpa remove' | Out-Null
        & $invoke -Command '/tpa spawn' | Out-Null
        & $invoke -Command '/tpa equip iron' | Out-Null
        & $invoke -Command '/tpa attack left 10' | Out-Null
        & $invoke -Command '/tpa stop' | Out-Null
        & $invoke -Command '/tpa attack right 10' | Out-Null
        & $invoke -Command '/tpa remove' | Out-Null
        $rebuilt = & $invoke -Command '/tpa spawn'
        Assert-Demo 'rebuild after death and interleaved actions' (-not $rebuilt.state.actor.dead -and
            $rebuilt.state.actor.life -eq 100 -and $rebuilt.state.actor.attackTicks -eq 0 -and
            $rebuilt.state.actor.hitCount -eq 0 -and @($rebuilt.state.actor.enemies).Count -eq 0) $rebuilt.state.actor
    }
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
