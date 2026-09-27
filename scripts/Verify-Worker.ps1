param(
    [string]$Uri = 'http://127.0.0.1:18080/mcp',
    [Parameter(Mandatory=$true)][string]$Token,
    [string]$Evidence = 'C:\RemoteControl\evidence\refactor'
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
New-Item -ItemType Directory -Force -Path $Evidence|Out-Null
$marker=Join-Path $Evidence ('request-started-'+[Guid]::NewGuid().ToString('N')+'.txt')
$script:requestId=0
function Call($name,$argumentsObject) {
    $script:requestId++
    $body=@{jsonrpc='2.0';id=$script:requestId;method='tools/call';params=@{name=$name;arguments=$argumentsObject}}|ConvertTo-Json -Depth 20 -Compress
    $raw=(Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Post -Headers @{'X-Admin-Token'=$Token;Accept='application/json, text/event-stream'} -ContentType 'application/json' -Body $body -TimeoutSec 15).Content
    $line=$raw -split "\n"|Where-Object {$_ -like 'data: *'}|Select-Object -Last 1
    $reply=if($line){$line.Substring(6)|ConvertFrom-Json}else{$raw|ConvertFrom-Json}
    if($reply.error -or $reply.result.isError){throw ($reply|ConvertTo-Json -Depth 20)}
    return $reply.result.content[0].text|ConvertFrom-Json
}
$before=Get-CimInstance Win32_Process -Filter "Name='RemoteControl.Service.exe'"|Where-Object {$_.CommandLine -match '--desktop-worker'}|Select-Object -First 1
if(-not $before -or $before.CommandLine -notmatch '--pipe"?\s+"?([^"\s]+)'){throw 'worker_pipe_not_found'}
$pipeName=$Matches[1]
$first=Call 'computer.run' @{runtime='powershell';session='disconnect-test';code='$retained=42'}
if(-not $first.ok){throw 'session_setup_failed'}
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
$pipe.Connect(5000)
$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true)
$writer.AutoFlush=$true
try {
    $code='$retained += 1; [IO.File]::WriteAllText('''+$marker.Replace("'", "''")+''',''started''); Start-Sleep -Milliseconds 1000'
    $request=@{op='run';args=@{runtime='powershell';session='disconnect-test';code=$code;timeoutMs=5000}}|ConvertTo-Json -Depth 10 -Compress
    $writer.WriteLine($request)
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    while(-not (Test-Path -LiteralPath $marker)) {
        if([DateTime]::UtcNow -ge $deadline){throw 'request_did_not_start'}
        Start-Sleep -Milliseconds 10
    }
} finally { $writer.Dispose(); $pipe.Dispose() }
$second=Call 'computer.run' @{runtime='powershell';session='disconnect-test';code='$retained'}
$after=Get-CimInstance Win32_Process -Filter "Name='RemoteControl.Service.exe'"|Where-Object {$_.CommandLine -match '--desktop-worker'}|Select-Object -First 1
$passed=$second.ok -and $second.stdout.Trim() -eq '43' -and $after.ProcessId -eq $before.ProcessId
$null=Call 'computer.close_session' @{runtime='powershell';session='disconnect-test'}
Remove-Item -LiteralPath $marker -ErrorAction SilentlyContinue
$receipt=@{timestamp=[DateTimeOffset]::UtcNow.ToString('o');passed=$passed;beforePid=$before.ProcessId;afterPid=$after.ProcessId;value=$second.stdout.Trim()}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Evidence 'pipe-disconnect.json') -Encoding UTF8
if(-not $passed){throw ($receipt|ConvertTo-Json -Compress)}
$receipt|ConvertTo-Json -Compress
