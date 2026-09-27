$ErrorActionPreference = 'Stop'
$service = Get-Service -Name 'RemoteControlSvc' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name 'RemoteControlSvc' -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}
