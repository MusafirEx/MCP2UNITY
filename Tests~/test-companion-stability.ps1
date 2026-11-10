#requires -Version 5.1
<#
.SYNOPSIS
  Read-only MCP transport soak and optional ngrok / user-initiated restart observation.
.EXAMPLE
  .\test-companion-stability.ps1 -ProjectRoot 'C:\path\to\UnityProject' -Iterations 12
.EXAMPLE
  .\test-companion-stability.ps1 -ProjectRoot 'C:\path\to\UnityProject' -Iterations 12 -TestPublicTunnel -ObserveRestart
#>
param(
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [ValidateRange(1,100)][int]$Iterations = 12,
    [switch]$TestPublicTunnel,
    [switch]$ObserveRestart,
    [ValidateRange(15,300)][int]$RestartWindowSeconds = 90
)
$ErrorActionPreference = 'Stop'
$statusRoot = Join-Path $ProjectRoot 'Library\WasimUnityMcpBridge\Companion'
$companionStatus = Join-Path $statusRoot 'companion-status.json'
$unityStatus = Join-Path $statusRoot 'unity-status.json'
$script:seq = 0
$script:StatusReadRecoveryCount = 0
$script:StatusReadRetryAttempts = 0

function Read-SharedJson([string]$Path) {
    # File.Exists followed by FileStream.Open is a TOCTOU race during Windows
    # replacement. Open directly, retry bounded transient file visibility/sharing
    # failures, and record every recovered read (never use stale cached status).
    $name = [System.IO.Path]::GetFileName($Path)
    $deadline = [DateTime]::UtcNow.AddMilliseconds(2000)
    $attempts = 0
    while ($true) {
        $stream = $null
        try {
            $stream = [System.IO.FileStream]::new(
                $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
                ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
            try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ([string]::IsNullOrWhiteSpace($json)) {
                throw [System.IO.IOException]::new('Status file was empty during update.')
            }
            # Malformed JSON is a hard error, not a transient missing-file pass.
            $status = ConvertFrom-Json -InputObject $json -ErrorAction Stop
            if ($attempts -gt 0) {
                $script:StatusReadRecoveryCount++
                $script:StatusReadRetryAttempts += $attempts
                Write-Warning ('Recovered temporary status read of ' + $name +
                    ' after ' + $attempts + ' retries.')
            }
            return $status
        } catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw ('Status file ' + $name + ' remained unavailable after 2 seconds (' +
                    $attempts + ' retries): ' + $_.Exception.GetType().Name)
            }
            $attempts++
            Start-Sleep -Milliseconds 25
        } catch [System.UnauthorizedAccessException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw ('Status file ' + $name + ' remained inaccessible after 2 seconds (' +
                    $attempts + ' retries): ' + $_.Exception.GetType().Name)
            }
            $attempts++
            Start-Sleep -Milliseconds 25
        } finally {
            if ($null -ne $stream) { $stream.Dispose() }
        }
    }
}

function Assert-State([bool]$Check, [string]$Message) {
    if (-not $Check) { throw $Message }
}

function Invoke-PrivateMcp([string]$Endpoint, [string]$Method, $Parameters) {
    $script:seq++
    $payload = [ordered]@{jsonrpc='2.0';id=$script:seq;method=$Method;params=$Parameters}
    # Never print endpoint, bearer token or private ngrok URL.
    $http = Invoke-WebRequest -UseBasicParsing -Uri $Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Headers @{ Accept='application/json, text/event-stream' } -Body (ConvertTo-Json -InputObject $payload -Depth 25 -Compress) -TimeoutSec 20
    Assert-State ([int]$http.StatusCode -eq 200) ('Unexpected HTTP status on ' + $Method)
    $rpc = $http.Content | ConvertFrom-Json
    Assert-State ($null -eq $rpc.error) ('JSON-RPC error on ' + $Method)
    return $rpc
}

function Test-RoundTrip([string]$Endpoint, [string]$Label) {
    Assert-State (-not [string]::IsNullOrWhiteSpace($Endpoint)) ($Label + ': endpoint unavailable')
    $initParams = [ordered]@{protocolVersion='2025-11-25';capabilities=[ordered]@{};clientInfo=[ordered]@{name='mcp2unity-stability-smoke';version='1.0'}}
    $init = Invoke-PrivateMcp $Endpoint 'initialize' $initParams
    Assert-State (-not [string]::IsNullOrWhiteSpace([string]$init.result.protocolVersion)) ($Label + ': initialize failed')
    $tools = Invoke-PrivateMcp $Endpoint 'tools/list' ([ordered]@{})
    $toolCount = @($tools.result.tools).Count
    Assert-State ($toolCount -eq 47) ($Label + ': expected 47 tools, got ' + $toolCount)
    $ping = Invoke-PrivateMcp $Endpoint 'ping' ([ordered]@{})
    Assert-State ($null -ne $ping.result) ($Label + ': ping failed')
    $rpc = Invoke-PrivateMcp $Endpoint 'tools/call' ([ordered]@{name='unity_get_status';arguments=[ordered]@{}})
    Assert-State ($rpc.result.isError -eq $false) ($Label + ': Unity IPC reported error')
    $unity = ([string]$rpc.result.content[0].text) | ConvertFrom-Json
    Assert-State ([bool]$unity.connected) ($Label + ': bridge not connected')
    Assert-State ([int]$unity.advertisedToolCount -eq 47) ($Label + ': tool count mismatch')
    Assert-State ([bool]$unity.unityIpcConnected) ($Label + ': Unity heartbeat not connected')
    return [pscustomobject]@{Label=$Label;ToolCount=$toolCount;BridgeVersion=[string]$unity.bridgeVersion}
}

function Assert-Heartbeat {
    $heartbeat = Read-SharedJson $unityStatus
    Assert-State (-not [string]::IsNullOrWhiteSpace([string]$heartbeat.timestampUtc)) 'Unity heartbeat missing'
    $age = ([DateTime]::UtcNow - [DateTime]::Parse([string]$heartbeat.timestampUtc).ToUniversalTime()).TotalSeconds
    Assert-State ($age -ge -3 -and $age -le 10) ('Unity heartbeat stale: ' + [Math]::Round($age,2) + 's')
}

Write-Host 'MCP2UNITY stability smoke: read-only, no scene edits or automatic service restarts.'
$initial = Read-SharedJson $companionStatus
Assert-State ([string]$initial.state -eq 'Running') 'Companion not running'
Assert-State ([int]$initial.processId -gt 0) 'Companion PID missing'
Assert-Heartbeat
Write-Host ('Companion running; ngrok=' + [string]$initial.ngrokState)
$first = Test-RoundTrip ([string]$initial.localEndpoint) 'local'
Write-Host ('PASS local MCP JSON-RPC: ' + $first.ToolCount + ' tools, bridge=' + $first.BridgeVersion)

if ($TestPublicTunnel) {
    Assert-State ([string]$initial.ngrokState -like 'Ready*') 'ngrok not ready'
    $public = Test-RoundTrip ([string]$initial.mcpEndpoint) 'public ngrok tunnel'
    Write-Host ('PASS public ngrok JSON-RPC: ' + $public.ToolCount + ' tools')
}

$durations = [System.Collections.Generic.List[double]]::new()
for ($i = 1; $i -le $Iterations; $i++) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $status = Read-SharedJson $companionStatus
    Assert-State ([string]$status.state -eq 'Running') ('Companion stopped at iteration ' + $i)
    Assert-State ([int]$status.processId -eq [int]$initial.processId) ('Companion PID changed unexpectedly at ' + $i)
    Assert-Heartbeat
    $null = Test-RoundTrip ([string]$status.localEndpoint) ('iteration ' + $i)
    $timer.Stop()
    $durations.Add($timer.Elapsed.TotalMilliseconds)
    Write-Host ('PASS local iteration ' + $i + '/' + $Iterations + ': ' + [Math]::Round($timer.Elapsed.TotalMilliseconds) + ' ms')
}
$avg = [Math]::Round(($durations | Measure-Object -Average).Average)
$worst = [Math]::Round(($durations | Measure-Object -Maximum).Maximum)
Write-Host ('PASS handshakes: ' + $Iterations + '/' + $Iterations + '; average ' + $avg + ' ms; max ' + $worst + ' ms')

if ($ObserveRestart) {
    Write-Host 'Restart observation armed. In Unity MCP Bridge click Restart Companion (or Stop then Start).'
    Write-Host ('This script observes for ' + $RestartWindowSeconds + ' seconds; it does not trigger a restart.')
    $deadline = [DateTime]::UtcNow.AddSeconds($RestartWindowSeconds)
    $observedPid = 0
    $restarted = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $candidate = Read-SharedJson $companionStatus
            if ([int]$candidate.processId -gt 0 -and
                [int]$candidate.processId -ne [int]$initial.processId) {
                $observedPid = [int]$candidate.processId
                # A new process can write Running before it finishes starting
                # its ngrok tunnel. Wait for complete readiness, not merely PID.
                if ([string]$candidate.state -eq 'Running' -and
                    (-not $TestPublicTunnel -or
                     ([string]$candidate.ngrokState -like 'Ready*' -and
                      -not [string]::IsNullOrWhiteSpace([string]$candidate.mcpEndpoint)))) {
                    $restarted = $candidate
                    break
                }
            }
        } catch { }
        Start-Sleep -Seconds 1
    }
    Assert-State ($observedPid -gt 0) 'New Companion PID not observed before deadline'
    Assert-State ($null -ne $restarted) 'New Companion never reached Running/ngrok readiness before deadline'
    Assert-Heartbeat
    $verified = Test-RoundTrip ([string]$restarted.localEndpoint) 'post-restart'
    Write-Host ('PASS post-restart local MCP: ' + $verified.ToolCount + ' tools; PID changed')
    if ($TestPublicTunnel) {
        Assert-State ([string]$restarted.ngrokState -like 'Ready*') 'Post-restart ngrok not ready'
        $verifiedPublic = Test-RoundTrip ([string]$restarted.mcpEndpoint) 'post-restart public ngrok'
        Write-Host ('PASS post-restart ngrok MCP: ' + $verifiedPublic.ToolCount + ' tools')
    }
}

Write-Host ('Status-file read recoveries: ' + $script:StatusReadRecoveryCount +
    ' (retry attempts: ' + $script:StatusReadRetryAttempts + ')')
if ($script:StatusReadRecoveryCount -gt 0) {
    Write-Warning 'MCP connectivity passed but status-file visibility had transient interruptions. Investigate if repeated.'
}
Write-Host 'PASS MCP2UNITY stability smoke complete. Private endpoint and token were not logged.'
