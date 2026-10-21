param([int]$FocusDelaySeconds = 3)
$ErrorActionPreference = 'Stop'
$invoke = Join-Path $PSScriptRoot 'invoke-test.ps1'
if ($FocusDelaySeconds -gt 0) {
    Write-Host "Switch to the test game within $FocusDelaySeconds seconds."
    Start-Sleep -Seconds $FocusDelaySeconds
}
try {
    $before = & $invoke -Command status
    if ($before.state.gameMenu -or -not $before.state.input.focused) { throw 'Enter the single-player test world and keep it focused.' }
    if ($before.state.input.chatLength -ne 0) { throw 'Existing chat text must be cleared manually before this probe.' }
    & $invoke -Command chat-open | Out-Null
    Start-Sleep -Milliseconds 100
    & $invoke -Command trace-start | Out-Null
    & $invoke -Command chat-type-status | Out-Null
    Start-Sleep -Milliseconds 400
    $typed = & $invoke -Command status
    $length = '/tpa status'.Length
    if ($typed.state.input.chatLength -ne $length -or $typed.state.input.receivedCharacters -ne $length) {
        throw "Character delivery failed: length=$($typed.state.input.chatLength), received=$($typed.state.input.receivedCharacters)."
    }
    if ($typed.state.chatProbe.executions -ne $before.state.chatProbe.executions) { throw 'Command executed before submission.' }
    Write-Host "PASS: $length characters reached IME and appeared in chat without execution."
    & $invoke -Command chat-submit-status | Out-Null
    Start-Sleep -Milliseconds 150
    $submitted = & $invoke -Command status
    if ($submitted.state.input.chatOpen -or $submitted.state.input.chatLength -ne 0 -or
        $submitted.state.chatProbe.executions -ne $before.state.chatProbe.executions + 1 -or
        $submitted.state.chatProbe.lastAction -ne 'status' -or
        $submitted.state.chatProbe.lastCaller -ne 'ChatCommandCaller') {
        throw 'Native chat submission did not execute the status command exactly once.'
    }
    $directory = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\test-results'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $path = Join-Path $directory ('chat-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
    @{passed=$true;typed=$typed.state;submitted=$submitted.state} | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $path -Encoding utf8
    Write-Host "PASS: native chat executed /tpa status once via ChatCommandCaller. Report: $path"
} finally {
    & $invoke -Command trace-stop | Out-Null
    & $invoke -Command chat-close | Out-Null
}
