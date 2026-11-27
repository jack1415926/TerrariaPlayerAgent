param(
    [Parameter(Mandatory)][string]$Command,
    [int]$TimeoutSeconds = 5
)
$ErrorActionPreference = 'Stop'
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\tModLoader\TPA'
if (-not (Test-Path -LiteralPath $directory)) { throw 'Start the test game with scripts/play.ps1 first.' }
$id = [guid]::NewGuid().ToString()
$channel = if ($Command.Trim() -eq '/tpa stop') { 'stop' } else { 'request-single' }
$request = Join-Path $directory "$channel-$id.json"
$response = Join-Path $directory "response-$id.json"
$temporary = Join-Path $directory "$id.tmp"
@{id=$id;command=$Command} | ConvertTo-Json -Compress | Set-Content -LiteralPath $temporary -Encoding utf8
[IO.File]::Move($temporary, $request)
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if (Test-Path -LiteralPath $response) {
        $result = Get-Content -LiteralPath $response -Raw | ConvertFrom-Json
        if ($result.id -eq $id) {
            Remove-Item -LiteralPath $response
            if (-not $result.ok) { throw $result.error }
            return $result
        }
    }
    Start-Sleep -Milliseconds 50
}
Remove-Item -LiteralPath $request -ErrorAction SilentlyContinue
throw 'No response from the game. Confirm scripts/play.ps1 was used. Execution outcome may be unknown; query status before retrying an action.'
