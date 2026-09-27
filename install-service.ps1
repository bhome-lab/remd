param(
    [string]$InstallDir = "$env:ProgramFiles\RemoteControl",
    [string]$Token = $env:REMOTE_CONTROL_TOKEN,
    [string]$Listen = "http://127.0.0.1:8080",
    [string]$FakerInputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Token)) { throw 'Pass -Token or set REMOTE_CONTROL_TOKEN.' }

$publish = $PSScriptRoot
if (-not (Test-Path (Join-Path $publish 'RemoteControl.Service.exe'))) {
    $publish = Join-Path $PSScriptRoot 'RemoteControl.Service\bin\Release\net10.0\win-x64\publish'
    dotnet publish (Join-Path $PSScriptRoot 'RemoteControl.Service\RemoteControl.Service.csproj') -c Release -r win-x64 --self-contained false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

if (Get-Service RemoteControlSvc -ErrorAction SilentlyContinue) { Stop-Service RemoteControlSvc }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
if ([IO.Path]::GetFullPath($publish).TrimEnd('\') -ne [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')) {
    Copy-Item (Join-Path $publish '*') $InstallDir -Recurse -Force
}
if ($FakerInputDirectory) {
    Copy-Item (Join-Path $FakerInputDirectory 'FakerInputWrapper.dll'), (Join-Path $FakerInputDirectory 'FakerInputDll.dll') $InstallDir -Force
}
$exe = Join-Path $InstallDir 'RemoteControl.Service.exe'

if (-not (Get-Service RemoteControlSvc -ErrorAction SilentlyContinue)) {
    New-Service -Name 'RemoteControlSvc' -BinaryPathName "`"$exe`" --urls $Listen" -DisplayName 'RemoteControl Service' -Description 'Local MCP computer control service' -StartupType Automatic | Out-Null
}
$service = Get-CimInstance Win32_Service -Filter "Name='RemoteControlSvc'"
$changed = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{PathName="`"$exe`" --urls $Listen";StartName='LocalSystem';StartMode='Automatic'}
if ($changed.ReturnValue -ne 0) { throw "Service configuration failed: $($changed.ReturnValue)" }
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\RemoteControlSvc' -Name Environment -PropertyType MultiString -Value @("REMOTE_CONTROL_TOKEN=$Token") -Force | Out-Null
Start-Service RemoteControlSvc
Write-Output "Installed RemoteControlSvc at $InstallDir"
