param([string]$Destination = (Join-Path $PSScriptRoot 'payload'))

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$ds4Revision = '62e4e59097156d75dda7a0def9120454809021b9'
$items = @(
    @{ Name='FakerInput_Setup_0.1.1_x64.msi'; Url='https://github.com/Ryochan7/FakerInput/releases/download/v0.1.1/FakerInput_Setup_0.1.1_x64.msi'; Sha256='4C0AEFB7340051A91D606776243298B5CD1143EF5508BBAE6800C474F9ED0840'; Signer='Ryodigi Solutions LLC' },
    @{ Name='FakerInputWrapper.dll'; Url="https://raw.githubusercontent.com/CircumSpector/DS4Windows/$ds4Revision/DS4Windows/libs/x64/FakerInputWrapper/FakerInputWrapper.dll"; Sha256='67D89F2BDB5909F67C466F74C792E6A6E244DF5686721FADA39D3F2FD52413D7'; Signer='Travis Nickles' },
    @{ Name='FakerInputDll.dll'; Url="https://raw.githubusercontent.com/CircumSpector/DS4Windows/$ds4Revision/DS4Windows/libs/x64/FakerInputWrapper/FakerInputDll.dll"; Sha256='0F87332917DABE83260A391551D18FF9D1EA7E3418003D6DFA6B1D2141B785FF'; Signer='Travis Nickles' }
)
foreach ($item in $items) {
    $path = Join-Path $Destination $item.Name
    if (-not (Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -Uri $item.Url -OutFile $path
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -ne $item.Sha256) { throw "SHA-256 mismatch: $($item.Name)" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "*$($item.Signer)*") {
        throw "Invalid Authenticode signature: $($item.Name)"
    }
    Write-Output "OK $($item.Name) $hash"
}
