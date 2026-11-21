param([string]$Version, [string]$Directory = 'artifacts', [string]$PrivateKeyPath)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$payload = Join-Path $Directory 'CosmoDriver.exe'
$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $pem = if ($PrivateKeyPath) { [IO.File]::ReadAllText($PrivateKeyPath) } else { $env:UPDATE_SIGNING_KEY }
    if (-not $pem) { throw 'UPDATE_SIGNING_KEY is required to publish an auto-update release' }
    $rsa.ImportFromPem($pem)
    $public = [System.Security.Cryptography.RSA]::Create()
    try {
        $public.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/CosmoDriver.Core/update-public.pem')))
        if ($rsa.ExportSubjectPublicKeyInfoPem() -cne $public.ExportSubjectPublicKeyInfoPem()) { throw 'Signing key does not match pinned application key' }
    } finally { $public.Dispose() }
    $manifest = [ordered]@{ Version = $Version; FileName = 'CosmoDriver.exe'; Sha256 = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash; Size = (Get-Item -LiteralPath $payload).Length }
    $data = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
    [IO.File]::WriteAllBytes((Join-Path $Directory 'update-manifest.json'), $data)
    $signature = $rsa.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes((Join-Path $Directory 'update-manifest.sig'), $signature)
} finally { $rsa.Dispose(); $pem = $null }
