param([string]$Version = '1.1.0', [switch]$SkipTests, [string]$Compiler)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot 'out'
$publish = Join-Path $output 'publish'
$payload = Join-Path $PSScriptRoot 'payload'

& (Join-Path $PSScriptRoot 'Prepare-Payload.ps1') -Destination $payload
if (-not $SkipTests) {
    dotnet test (Join-Path $root 'RemoteControl.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
dotnet publish (Join-Path $root 'RemoteControl.Service\RemoteControl.Service.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$setup = Join-Path $output 'RemoteControl-Setup.exe'
if (-not $Compiler) {
    $candidates = @(
        (Join-Path $payload 'nsis-tool\makensis.exe'),
        'C:\Program Files (x86)\NSIS\makensis.exe'
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler) {
    winget download --id NSIS.NSIS --version 3.12 --exact --download-directory $payload --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw 'NSIS download failed.' }
    $archive = Get-ChildItem -LiteralPath $payload -Filter '*3.12*Machine*exe' | Select-Object -First 1 -ExpandProperty FullName
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '3BC2B06253A7E4957111BE152AC6A536E0C7478A706E19DA814038DB5D706495') {
        throw 'NSIS compiler hash mismatch.'
    }
    $sevenZip = @('C:\Program Files\7-Zip\7z.exe', 'C:\Program Files (x86)\7-Zip\7z.exe') |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $sevenZip) { throw '7-Zip is required to unpack the verified NSIS compiler.' }
    & $sevenZip x $archive "-o$(Join-Path $payload 'nsis-tool')" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'NSIS extraction failed.' }
    $Compiler = Join-Path $payload 'nsis-tool\makensis.exe'
}
$compilerVersion = (& $Compiler /VERSION | Select-Object -First 1).Trim()
if ($compilerVersion -ne 'v3.12') { throw "NSIS 3.12 is required; found $compilerVersion" }
& $Compiler "/DVERSION=$Version" "/DPUBLISH=$publish" "/DPAYLOAD=$payload" "/DOUTPUT=$setup" `
    (Join-Path $PSScriptRoot 'RemoteControl.nsi')
if ($LASTEXITCODE -ne 0) { throw 'NSIS build failed.' }
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer not found: $setup" }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$setup.sha256" -Encoding ascii -Value "$hash *$(Split-Path $setup -Leaf)"
Write-Output "SHA256 $hash $setup"
