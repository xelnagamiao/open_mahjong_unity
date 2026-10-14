[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$Python,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$sgRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sgPlayer=(Resolve-Path -LiteralPath $Player).Path
$sgPython=(Resolve-Path -LiteralPath $Python).Path
$sgOutput=[IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $sgOutput) -and (Get-ChildItem -LiteralPath $sgOutput -Force | Select-Object -First 1)) {
    throw 'OutputDirectory must be new or empty.'
}
New-Item -ItemType Directory -Path $sgOutput -Force | Out-Null
$sgEvidence=Join-Path $sgOutput 'evidence'
$sgServer=$null
$sgClients=@()
try {
    $sgScript=Join-Path $sgRepo 'open_mahjong_server/scripts/validate_singapore_clients.py'
    $sgServer=Start-Process -FilePath $sgPython -ArgumentList @('-X','utf8',('"'+$sgScript+'"'),'--output-dir',('"'+$sgEvidence+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $sgOutput 'server.out.log') -RedirectStandardError (Join-Path $sgOutput 'server.err.log')
    $sgDeadline=(Get-Date).AddSeconds($TimeoutSeconds)
    $sgEndpointPath=Join-Path $sgEvidence 'endpoint.txt'
    while (!(Test-Path -LiteralPath $sgEndpointPath)) {
        if ($sgServer.HasExited -or (Get-Date) -gt $sgDeadline) { throw 'Loopback server did not start; see server.err.log.' }
        Start-Sleep -Milliseconds 250
    }
    $sgEndpoint=(Get-Content -LiteralPath $sgEndpointPath -Raw).Trim()
    foreach ($sgUid in 11000001..11000004) {
        $sgClientOutput=Join-Path $sgEvidence "player-$sgUid"
        New-Item -ItemType Directory -Path $sgClientOutput | Out-Null
        $sgArguments=@('--verify-singapore',('"'+$sgClientOutput+'"'),('"'+$sgEndpoint+'"'),"$sgUid",'-logFile',('"'+$sgClientOutput+'/player.log"'),'-screen-width','1280','-screen-height','720','-screen-fullscreen','0')
        $sgProcess=Start-Process -FilePath $sgPlayer -ArgumentList $sgArguments -WindowStyle Hidden -PassThru
        $sgClients+=@{uid=$sgUid;process=$sgProcess;output=$sgClientOutput}
    }
    $sgNextProgress=(Get-Date).AddSeconds(30)
    while (!$sgServer.HasExited -or ($sgClients | Where-Object { !$_.process.HasExited })) {
        if ((Get-Date) -gt $sgDeadline) { throw 'Four-client validation exceeded the deadline.' }
        if ((Get-Date) -gt $sgNextProgress) {
            Write-Host "Singapore clients are running. Evidence: $sgEvidence"
            $sgNextProgress=(Get-Date).AddSeconds(30)
        }
        Start-Sleep -Milliseconds 500
    }
    $sgServerResult=Get-Content -LiteralPath (Join-Path $sgEvidence 'server-result.json') -Raw | ConvertFrom-Json
    $sgResults=@($sgClients | ForEach-Object {
        Get-Content -LiteralPath (Join-Path $_.output 'result.json') -Raw | ConvertFrom-Json
    })
    $sgPassed=$sgServerResult.status -eq 'completed' -and $sgResults.Count -eq 4 -and !($sgResults | Where-Object { $_.status -ne 'completed' -or $_.ends -ne 1 -or $_.failures.Count -gt 0 })
    @{passed=$sgPassed;server=$sgServerResult;clients=$sgResults} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $sgOutput 'fleet-result.json') -Encoding UTF8
    if (!$sgPassed) { throw "Client validation failed; inspect $sgOutput/fleet-result.json." }
    Write-Host "Four Singapore clients passed. Report: $sgOutput/fleet-result.json"
} finally {
    foreach ($sgOwned in @($sgServer)+@($sgClients | ForEach-Object { $_.process })) {
        if ($null -ne $sgOwned -and !$sgOwned.HasExited) { Stop-Process -Id $sgOwned.Id -ErrorAction SilentlyContinue }
    }
}
