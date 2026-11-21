$ErrorActionPreference = 'Stop'
[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version
$installer = (Resolve-Path "artifacts/CosmoDriver-Setup-$version-win-x64.exe").Path
$uninstallKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{C74D0BAE-1854-4B7E-91CE-935AC9EC44F5}_is1'
if (Test-Path $uninstallKey) { throw 'An installed CosmoDriver already exists: refusing to alter it during package tests' }
$folder = Join-Path (Resolve-Path artifacts).Path ('install-test-' + [guid]::NewGuid().ToString('N'))
$process = Start-Process $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',('/DIR="' + $folder + '"') -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode) { throw 'Installer failed' }
try {
    $installed = Join-Path $folder CosmoDriver.exe
    if ((Get-FileHash $installed).Hash -ne (Get-FileHash artifacts/CosmoDriver.exe).Hash) { throw 'Installed executable mismatch' }
    $smoke = Join-Path (Resolve-Path artifacts).Path smoke-installed
    $process = Start-Process $installed -ArgumentList '--smoke',$smoke -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode) { throw 'Installed app smoke failed' }
} finally {
    $uninstaller = Join-Path $folder unins000.exe
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode) { throw 'Uninstaller failed' }
    }
}
if (Test-Path $uninstallKey) { throw 'Uninstall registry entry was not removed' }
if (Test-Path -LiteralPath (Join-Path $folder CosmoDriver.exe)) { throw 'Installed app was not removed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "artifacts/CosmoDriver-Portable-$version-win-x64.zip").Path)
try {
    $names = @($zip.Entries.FullName)
    if ($names.Count -ne 3 -or 'CosmoDriver.exe' -notin $names -or 'README.md' -notin $names -or 'LICENSE' -notin $names) { throw 'Unexpected portable package contents' }
} finally { $zip.Dispose() }
Write-Output 'PASS installer, installed app smoke, uninstall and portable ZIP contents'
