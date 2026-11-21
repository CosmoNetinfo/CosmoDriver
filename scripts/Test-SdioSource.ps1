$ErrorActionPreference = 'Stop'
$out = Join-Path (Resolve-Path artifacts).Path 'sdio-source-test'
New-Item -ItemType Directory -Force $out | Out-Null
$archive = Join-Path $out official.zip
$uri = 'https://www.glenn.delahoy.com/downloads/sdio/SDIO_2.0.8.891.zip'
$expected = '96F48D84D97EBDA6E60FD67C45EAF388AE7D3B3DB8E171D3C5D7E5381065BA89'
try {
    Invoke-WebRequest -Uri $uri -OutFile $archive -TimeoutSec 60
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -cne $expected) { throw 'Downloaded SDIO archive hash mismatch' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if ('SDIO_x64_R891.exe' -notin $zip.Entries.FullName) { throw 'Expected SDIO engine missing' }
    } finally { $zip.Dispose() }
    @{Success=$true; Version='2.0.8.891'; Source=$uri; Sha256=$expected; Executed=$false} | ConvertTo-Json | Set-Content artifacts/sdio-source-results.json
    Write-Output 'PASS official SDIO download, pinned archive hash and expected engine entry; engine not executed'
} catch {
    @{Success=$false; Source=$uri; Error=$_.Exception.Message; Executed=$false} | ConvertTo-Json | Set-Content artifacts/sdio-source-results.json
    throw
}
