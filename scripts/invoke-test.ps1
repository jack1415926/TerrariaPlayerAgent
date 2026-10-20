param(
    [Parameter(Mandatory)][string]$Command,
    [int]$TimeoutSeconds = 5
)
$ErrorActionPreference = 'Stop'
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\tModLoader\TPA'
if (-not (Test-Path -LiteralPath $directory)) { throw 'Start the test game with scripts/play.ps1 first.' }
$request = Join-Path $directory 'request.json'
if (Test-Path -LiteralPath $request) { throw 'A request is pending. Do not submit simultaneous commands.' }
$id = [guid]::NewGuid().ToString()
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
if (Test-Path -LiteralPath $request) {
    $pending = Get-Content -LiteralPath $request -Raw | ConvertFrom-Json
    if ($pending.id -eq $id) { Remove-Item -LiteralPath $request }
}
throw 'No response from the game. Confirm scripts/play.ps1 was used. Execution outcome may be unknown; query status before retrying an action.'
