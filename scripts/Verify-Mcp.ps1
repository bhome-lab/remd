param(
    [string]$Uri = 'http://127.0.0.1:18080/mcp',
    [Parameter(Mandatory=$true)][string]$Token,
    [string]$Evidence = 'C:\RemoteControl\evidence\regression'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
$headers = @{'X-Admin-Token'=$Token;Accept='application/json, text/event-stream'}
$script:id=0
$script:passed=0
function Check($condition,$label) {
    if(-not $condition) { throw "FAIL: $label" }
    $script:passed++
    "PASS: $label"
}
function Rpc($method,$parameters) {
    $script:id++
    $body=@{jsonrpc='2.0';id=$script:id;method=$method;params=$parameters}|ConvertTo-Json -Depth 30 -Compress
    $raw=(Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 45).Content
    $data=($raw -split "`n" | Where-Object {$_ -like 'data: *'} | Select-Object -Last 1)
    $reply=if($data){$data.Substring(6)|ConvertFrom-Json}else{$raw|ConvertFrom-Json}
    if($reply.error){throw ($reply.error|ConvertTo-Json -Compress)}
    return $reply.result
}
function Call($name,$argumentsObject) {
    $reply=Rpc 'tools/call' @{name=$name;arguments=$argumentsObject}
    $image=$reply.content|Where-Object type -eq 'image'|Select-Object -First 1
    if($image) {
        $path=Join-Path $Evidence ($name.Replace('.','-')+'.jpg')
        [IO.File]::WriteAllBytes($path,[Convert]::FromBase64String($image.data))
        $value=@{mimeType=$image.mimeType;bytes=(Get-Item -LiteralPath $path).Length;path=$path}
    } else {
        $value=try{$reply.content[0].text|ConvertFrom-Json}catch{$reply.content[0].text}
    }
    @{timestamp=[DateTimeOffset]::UtcNow.ToString('o');tool=$name;arguments=$argumentsObject;result=$value}|ConvertTo-Json -Depth 30 -Compress|Add-Content -LiteralPath (Join-Path $Evidence 'calls.jsonl') -Encoding UTF8
    return $value
}
$init=Rpc 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='remd-e2e';version='1'}}
Check ($null -ne $init.serverInfo) 'MCP initialize'
$list=Rpc 'tools/list' @{}
$list|ConvertTo-Json -Depth 30|Set-Content -LiteralPath (Join-Path $Evidence 'tools.json') -Encoding UTF8
Check ($list.tools.Count -eq 21) '21 MCP tools'
$status=Call 'computer.status' @{}
Check ($status.worker.state -eq 'running' -and $status.input.driver.connected -and $status.input.driver.signed) 'worker + signed FakerInput'
$windows=Call 'computer.windows' @{}
$game=$windows|Where-Object title -eq 'Path of Exile'|Select-Object -First 1
Check ($null -ne $game) 'live Path of Exile window'
foreach($capture in @(
    @{name='computer.screenshot';params=@{}},
    @{name='computer.screenshot_monitor';params=@{monitor=0}},
    @{name='computer.screenshot_window';params=@{windowId=$game.handle}},
    @{name='computer.screenshot_region';params=@{x=0;y=0;width=320;height=200}}
)) {
    $shot=Call $capture.name $capture.params
    Check ($shot.mimeType -eq 'image/jpeg' -and $shot.bytes -gt 1000) $capture.name
}
$specs=@(
 @{runtime='powershell';first='$remd=40; $env:USERNAME';second='$remd+2';direct='"direct"';timeout='Start-Sleep -Seconds 10';large='"x"*131072'},
 @{runtime='nodejs';first='let remd=40; console.log(process.env.USERNAME)';second='console.log(remd+2)';direct='process.stdout.write("direct")';timeout='while(true){}';large='console.log("x".repeat(131072))'},
 @{runtime='python';first='import os; remd=40; print(os.environ["USERNAME"])';second='print(remd+2)';direct='import os; os.write(1,b"direct")';timeout='import time; time.sleep(10)';large='print("x"*131072)'}
)
foreach($spec in $specs) {
    $null=Call 'computer.close_session' @{runtime=$spec.runtime;session='e2e'}
    $a=Call 'computer.run' @{runtime=$spec.runtime;session='e2e';code=$spec.first}
    Check ($a.ok -and $a.stdout.Trim() -eq 'Admin') "$($spec.runtime) user"
    $b=Call 'computer.run' @{runtime=$spec.runtime;session='e2e';code=$spec.second}
    Check ($b.ok -and $b.stdout.Trim() -eq '42') "$($spec.runtime) persistent state"
    $d=Call 'computer.run' @{runtime=$spec.runtime;session='e2e';code=$spec.direct}
    Check ($d.ok -and $d.stdout.Trim() -eq 'direct') "$($spec.runtime) direct stdout"
    $large=Call 'computer.run' @{runtime=$spec.runtime;code=$spec.large}
    Check ($large.ok -and $large.stdout.Trim().Length -eq 131072) "$($spec.runtime) large output"
    $timed=Call 'computer.run' @{runtime=$spec.runtime;session='timeout';code=$spec.timeout;timeoutMs=300}
    Check (-not $timed.ok -and $timed.error -eq 'execution_timeout' -and $timed.durationMs -lt 5000) "$($spec.runtime) timeout"
    $again=Call 'computer.run' @{runtime=$spec.runtime;session='timeout';code=$spec.direct}
    Check ($again.ok -and $again.stdout.Trim() -eq 'direct') "$($spec.runtime) recovery"
    Check (Call 'computer.close_session' @{runtime=$spec.runtime;session='e2e'}) "$($spec.runtime) close session"
    $null=Call 'computer.close_session' @{runtime=$spec.runtime;session='timeout'}
}
$py=Call 'computer.environment' @{runtime='python';name='e2e-python';packages=@('six==1.17.0')}
Check ($py.ok -and $py.path -like 'C:\Users\Admin\*') 'Python pip environment under user profile'
$pyRun=Call 'computer.run' @{runtime='python';environment='e2e-python';session='env';code='import six; print(six.__version__)'}
Check ($pyRun.ok -and $pyRun.stdout.Trim() -eq '1.17.0') 'Python installed package'
$pyReuse=Call 'computer.run' @{runtime='python';session='env';code='import six; print(six.__version__)'}
Check ($pyReuse.ok -and $pyReuse.stdout.Trim() -eq '1.17.0') 'Python session retains environment when omitted'
$node=Call 'computer.environment' @{runtime='nodejs';name='e2e-node';packages=@('is-number@7.0.0')}
Check ($node.ok -and $node.path -like 'C:\Users\Admin\*') 'Node npm environment under user profile'
$nodeRun=Call 'computer.run' @{runtime='nodejs';environment='e2e-node';session='env';code='console.log(require("is-number")(42))'}
Check ($nodeRun.ok -and $nodeRun.stdout.Trim() -eq 'true') 'Node installed package'
$lua=Call 'computer.lua' @{code='n=1; return n';session='e2e'}
$lua2=Call 'computer.lua' @{code='return n+41';session='e2e'}
Check ($lua.ok -and $lua2.ok -and $lua2.output -eq '42') 'Lua persistent state'
$partial=Call 'computer.run' @{runtime='python';session='framing';code='print("before"); raise ValueError("boom")'}
Check (-not $partial.ok -and $partial.stdout.Trim() -eq 'before' -and $partial.stderr -like '*ValueError*') 'Python keeps output before exception'
$marker=Call 'computer.run' @{runtime='python';session='framing';code='import os; os.write(1,b"__REMOTE_CONTROL_RESULT__plain log\n"); print("real result")'}
Check ($marker.ok -and $marker.stdout -like '*plain log*real result*') 'Python ordinary stdout cannot impersonate protocol'
$markerNext=Call 'computer.run' @{runtime='python';session='framing';code='print(42)'}
Check ($markerNext.ok -and $markerNext.stdout.Trim() -eq '42') 'Python framing remains aligned'
$null=Call 'computer.close_session' @{runtime='python';session='framing'}
$drain=Call 'computer.run' @{runtime='python';code='import subprocess,sys; subprocess.Popen([sys.executable,"-c","import time;time.sleep(10)"])';timeoutMs=1000}
Check (-not $drain.ok -and $drain.error -eq 'execution_timeout' -and $drain.durationMs -lt 5000) 'Timeout covers inherited stdout drain'
$luaError=Call 'computer.lua' @{code='return run("python", "raise ValueError(123)")'}
Check (-not $luaError.ok) 'Lua propagates runtime failure'
$luaTimeout=Call 'computer.lua' @{code='while true do end';session='bounded'}
Check (-not $luaTimeout.ok -and $luaTimeout.error -eq 'execution_timeout') 'Lua infinite loop times out'
$luaRecovered=Call 'computer.lua' @{code='return 42';session='bounded'}
Check ($luaRecovered.ok -and $luaRecovered.output -eq '42') 'Lua recovers after timeout'
$bad=Call 'computer.raw_keyboard' @{report='AA=='}
Check (-not $bad.ok -and $bad.error -eq 'invalid_report') 'invalid raw report'
$bad=Call 'computer.hotkey' @{keys='UNKNOWN+A'}
Check (-not $bad.ok -and $bad.error -eq 'invalid_key') 'invalid named key'
$bad=Call 'computer.press' @{key=[string][char]0x0444}
Check (-not $bad.ok -and $bad.error -eq 'invalid_key') 'Unicode letter is not a named HID key'
$released=Call 'computer.release_all' @{}
Check $released.ok 'release all'
@{passed=$script:passed;timestamp=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Evidence 'summary.json') -Encoding UTF8
"Passed: $script:passed"
