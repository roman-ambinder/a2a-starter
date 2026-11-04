#Requires -Version 7
# Builds once, then starts MockEntra (5100), WebhookReceiver (5300) and AgentServer (5200..5204).
# Ctrl+C stops all three. Then run:  dotnet run --project samples/AgentClient -- all
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$waitSeconds = if ($env:WAIT_SECONDS) { [int]$env:WAIT_SECONDS } else { 60 }
$procs = @()

# Runs the built DLL directly, so the process we track is the app itself (no `dotnet run` host that could be killed alone).
function Start-App($name) {
    Write-Host "Starting $name..."
    $dll = Join-Path $PSScriptRoot "samples/$name/bin/Debug/net10.0/Ambinder.A2A.Samples.$name.dll"
    Start-Process -FilePath dotnet -NoNewWindow -PassThru -ArgumentList @($dll)
}

function Wait-Ready($name, $url, $proc) {
    for ($i = 0; $i -lt $waitSeconds; $i++) {
        try {
            Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 | Out-Null
            Write-Host "$name is ready ($url)"
            return
        } catch { }
        if ($proc.HasExited) { throw "$name exited before it was ready." }
        Start-Sleep -Seconds 1
    }
    throw "$name was not ready at $url after ${waitSeconds}s."
}

foreach ($port in 5100, 5200, 5201, 5202, 5203, 5204, 5300) {
    $busy = if ($IsWindows) { Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue }
            else { & lsof -nP "-iTCP:$port" -sTCP:LISTEN 2>$null }
    if ($busy) { throw "Port $port is already in use. Stop the other process (an earlier run-all?) and try again." }
}

try {
    dotnet build Ambinder.A2A.slnx --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    $entra = Start-App 'MockEntra'; $procs += $entra
    Wait-Ready 'MockEntra' 'http://localhost:5100/health/ready' $entra
    $hook = Start-App 'WebhookReceiver'; $procs += $hook
    Wait-Ready 'WebhookReceiver' 'http://localhost:5300/health/ready' $hook
    $server = Start-App 'AgentServer'; $procs += $server
    Wait-Ready 'AgentServer' 'http://localhost:5200/health/ready' $server

    Write-Host @'

All three apps are running.
  MockEntra        http://localhost:5100
  AgentServer      http://localhost:5200  (gRPC 5201 echo, 5202 report, 5203 booking, 5204 secure)
  WebhookReceiver  http://localhost:5300
  Agent card       http://localhost:5200/.well-known/agent-card.json

Try:  dotnet run --project samples/AgentClient -- all
Press Ctrl+C to stop.
'@
    while (-not ($procs | Where-Object HasExited)) { Start-Sleep -Seconds 1 }
    throw 'An app exited unexpectedly.'
}
finally {
    Write-Host 'Stopping...'
    foreach ($p in $procs) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
}
