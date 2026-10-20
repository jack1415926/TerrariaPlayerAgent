param([int]$FocusDelaySeconds = 3)
$ErrorActionPreference = 'Stop'
$invoke = Join-Path $PSScriptRoot 'invoke-test.ps1'
try {
    & $invoke -Command chat-close | Out-Null
    & $invoke -Command trace-start | Out-Null
    if ($FocusDelaySeconds -gt 0) {
        Write-Host "Switch to the test game within $FocusDelaySeconds seconds."
        Start-Sleep -Seconds $FocusDelaySeconds
    }
    & $invoke -Command enter | Out-Null
    Start-Sleep -Milliseconds 300
    $result = & $invoke -Command status
    $directory = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\test-results'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $path = Join-Path $directory ('enter-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    if (-not $result.state.input.chatOpen -or $result.state.input.downSamples -lt 1) {
        throw "FAIL: native Enter did not open chat. Report: $path"
    }
    Write-Host "PASS: game-local SDL Enter opened chat; downSamples=$($result.state.input.downSamples). Report: $path"
} finally {
    & $invoke -Command chat-close | Out-Null
    & $invoke -Command trace-stop | Out-Null
}
