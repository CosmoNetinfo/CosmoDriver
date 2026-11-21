param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version' }
if ((Get-Content src/CosmoDriver.Core/UpdateTrust.cs -Raw) -notmatch ('new\(' + ($Version.Replace('.', ', ')) + '\)')) { throw 'UpdateTrust.CurrentVersion must match release version' }
dotnet build src/CosmoDriver/CosmoDriver.csproj -c Release -p:Version=$Version
if ($LASTEXITCODE) { throw 'Build failed' }
dotnet run --project tests/CosmoDriver.Tests -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }
dotnet publish src/CosmoDriver/CosmoDriver.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o artifacts/publish
if ($LASTEXITCODE) { throw 'Publish failed' }
New-Item -ItemType Directory -Force artifacts | Out-Null
Copy-Item -LiteralPath artifacts/publish/CosmoDriver.exe -Destination artifacts/CosmoDriver.exe -Force
Copy-Item -LiteralPath marketing/CosmoDriver-pubblicita-CosmoNet.png -Destination artifacts/CosmoDriver-pubblicita-CosmoNet.png -Force
Compress-Archive -Path artifacts/publish/CosmoDriver.exe,README.md,LICENSE -DestinationPath "artifacts/CosmoDriver-Portable-$Version-win-x64.zip" -Force
$compiler = @("${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 6/ISCC.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is required' }
& $compiler "/DAppVersion=$Version" installer/CosmoDriver.iss
if ($LASTEXITCODE) { throw 'Installer build failed' }
Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip','.png' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.Name } | Set-Content artifacts/SHA256SUMS.txt
