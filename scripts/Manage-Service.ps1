param(
    [Parameter(Mandatory=$true)][ValidateSet('Install','Uninstall')][string]$Action,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'RemoteControl'),
    [string]$Listen = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
$name = 'RemoteControlSvc'
$existing = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $name -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}
if ($Action -eq 'Uninstall') {
    if ($existing) {
        & "$env:WINDIR\System32\sc.exe" delete $name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Service removal failed: $LASTEXITCODE" }
    }
    exit 0
}

$exe = Join-Path $InstallDir 'RemoteControl.Service.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Executable not found: $exe" }
$binaryPath = '"' + $exe + '" --urls ' + $Listen
if (-not $existing) {
    New-Service -Name $name -BinaryPathName $binaryPath -DisplayName 'RemoteControl Service' `
        -Description 'Local MCP computer control service' -StartupType Automatic | Out-Null
}
$service = Get-CimInstance Win32_Service -Filter "Name='$name'"
$change = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{
    PathName=$binaryPath;StartName='LocalSystem';StartMode='Automatic'
}
if ($change.ReturnValue -ne 0) { throw "Service configuration failed: $($change.ReturnValue)" }
Start-Service -Name $name
$running = Get-Service -Name $name
$running.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
Write-Output 'RemoteControlSvc is running.'
