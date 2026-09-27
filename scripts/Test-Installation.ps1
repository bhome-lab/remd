param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'RemoteControl'),
    [string]$Uri = 'http://127.0.0.1:8080/mcp',
    [string]$TokenFile = (Join-Path $env:ProgramData 'RemoteControl\control.token'),
    [switch]$Json,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [bool]$ok, [string]$detail, [bool]$required = $true) {
    $checks.Add([pscustomobject]@{name=$name;ok=$ok;required=$required;detail=$detail})
}
function Call([string]$name, [hashtable]$argumentsObject = @{}) {
    $body = @{jsonrpc='2.0';id=1;method='tools/call';params=@{name=$name;arguments=$argumentsObject}} |
        ConvertTo-Json -Depth 12 -Compress
    $raw = (Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Post -TimeoutSec 15 `
        -Headers @{'X-Admin-Token'=$token;Accept='application/json, text/event-stream'} `
        -ContentType 'application/json' -Body $body).Content
    $line = $raw -split "`n" | Where-Object { $_ -like 'data: *' } | Select-Object -Last 1
    $reply = if ($line) { $line.Substring(6) | ConvertFrom-Json } else { $raw | ConvertFrom-Json }
    if ($reply.error -or $reply.result.isError) { throw "MCP call failed: $name" }
    return $reply.result.content[0].text | ConvertFrom-Json
}

$exe = Join-Path $InstallDir 'RemoteControl.Service.exe'
Check 'diagnostic process' ([Environment]::Is64BitProcess) '64-bit PowerShell'
Check 'application' (Test-Path -LiteralPath $exe) $exe
foreach ($file in 'FakerInputWrapper.dll','FakerInputDll.dll') {
    $path = Join-Path $InstallDir $file
    $signature = if (Test-Path -LiteralPath $path) { Get-AuthenticodeSignature -LiteralPath $path } else { $null }
    Check $file ($null -ne $signature -and $signature.Status -eq 'Valid') 'Authenticode signature'
}

$service = Get-CimInstance Win32_Service -Filter "Name='RemoteControlSvc'" -ErrorAction SilentlyContinue
Check 'service' ($null -ne $service -and $service.State -eq 'Running') "state=$($service.State)"
$systemDirectory = if ([Environment]::Is64BitProcess) { 'System32' } else { 'Sysnative' }
$pnputil = Join-Path $env:WINDIR "$systemDirectory\pnputil.exe"
for ($attempt = 0; $attempt -lt 15; $attempt++) {
    $driver = & $pnputil /enum-devices /connected /deviceid 'ROOT\FakerInput' 2>&1 | Out-String
    $present = $driver -match 'FakerInput' -and $driver -notmatch 'No devices were found'
    if ($present) { break }
    Start-Sleep -Seconds 2
}
Check 'FakerInput device' $present 'ROOT\FakerInput connected'

$token = if (Test-Path -LiteralPath $TokenFile) { (Get-Content -LiteralPath $TokenFile -Raw).Trim() } else { $env:REMOTE_CONTROL_TOKEN }
Check 'control token' (-not [string]::IsNullOrWhiteSpace($token)) 'Token file or REMOTE_CONTROL_TOKEN'

if ($service.State -eq 'Running' -and $token) {
    try {
        $health = $null
        $status = $null
        $lastError = ''
        for ($attempt = 0; $attempt -lt 15; $attempt++) {
            try {
                $health = Invoke-RestMethod -Uri ($Uri -replace '/mcp$', '/healthz') -TimeoutSec 5
                if ($health.ok) { $status = Call 'computer.status' }
                if ($status.worker.state -eq 'running' -and $status.input.driver.connected) { break }
            } catch { $lastError = $_.Exception.Message }
            Start-Sleep -Seconds 2
        }
        Check 'HTTP health' ($health.ok -eq $true) 'GET /healthz'
        if (-not $status) { throw "MCP not ready: $lastError" }
        Check 'desktop worker' ($status.worker.state -eq 'running') "state=$($status.worker.state); error=$($status.worker.error)"
        Check 'FakerInput connection' ($status.input.driver.connected -and $status.input.driver.signed) "error=$($status.input.driver.error)"
        foreach ($runtime in 'nodejs','python') {
            $available = $status.runtimes.$runtime -eq $true
            $code = if ($runtime -eq 'nodejs') { 'console.log(42)' } else { 'print(42)' }
            $result = Call 'computer.run' @{runtime=$runtime;code=$code}
            $valid = if ($available) { $result.ok -and $result.stdout.Trim() -eq '42' }
                else { -not $result.ok -and $result.error -eq 'environment_unavailable' }
            Check $runtime $valid $(if ($available) { 'available; execution OK' } else { 'not installed; API returns environment_unavailable' }) $false
        }
        if ($status.worker.state -eq 'running') {
            try {
                $body = @{jsonrpc='2.0';id=2;method='tools/call';params=@{name='computer.screenshot_region';arguments=@{x=0;y=0;width=16;height=16}}} | ConvertTo-Json -Depth 12 -Compress
                $raw = (Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Post -TimeoutSec 15 `
                    -Headers @{'X-Admin-Token'=$token;Accept='application/json, text/event-stream'} `
                    -ContentType 'application/json' -Body $body).Content
                Check 'screen capture' ($raw -match 'image/jpeg' -and $raw -notmatch '"isError":true') '16x16 JPEG'
            } catch { Check 'screen capture' $false $_.Exception.Message }
        }
    } catch {
        Check 'MCP' $false $_.Exception.Message
    }
}

$passed = @($checks | Where-Object { $_.required -and -not $_.ok }).Count -eq 0
$result = [pscustomobject]@{ok=$passed;checks=$checks}
if ($ReportPath) {
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if ($Json) { $result | ConvertTo-Json -Depth 6 } else {
    foreach ($check in $checks) {
        $state = if ($check.ok) { 'OK' } elseif ($check.required) { 'FAIL' } else { 'OPTIONAL' }
        Write-Output "$state $($check.name): $($check.detail)"
    }
}
if (-not $passed) { exit 1 }
