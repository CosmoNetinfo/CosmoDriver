param([string]$PrivateKeyPath)
$ErrorActionPreference = 'Stop'
$root = Join-Path (Resolve-Path artifacts).Path ('update-e2e-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root package
$targetFolder = Join-Path $root target
New-Item -ItemType Directory -Path $package,$targetFolder | Out-Null
Copy-Item artifacts/CosmoDriver.exe (Join-Path $package CosmoDriver.exe)
Copy-Item artifacts/CosmoDriver.exe (Join-Path $root CosmoDriver.Update.exe)
$target = Join-Path $targetFolder CosmoDriver.exe
[IO.File]::WriteAllText($target, 'old-version-sentinel')
[xml]$props = Get-Content Directory.Build.props
$current = [version]$props.Project.PropertyGroup.Version
$next = '{0}.{1}.{2}' -f $current.Major,$current.Minor,($current.Build + 1)
& "$PSScriptRoot/Sign-Release.ps1" -Version $next -Directory $package -PrivateKeyPath $PrivateKeyPath
$plan = @{ Directory=$package; Target=$target; ParentId=[int]::MaxValue; ParentStart=0; Restart=$false } | ConvertTo-Json -Compress
$planPath = Join-Path $package plan.json
[IO.File]::WriteAllText($planPath,$plan)
$process = Start-Process (Join-Path $root CosmoDriver.Update.exe) -ArgumentList '--apply-update',$planPath -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode) { throw 'Valid signed auto-update was rejected' }
if ((Get-FileHash $target).Hash -ne (Get-FileHash artifacts/CosmoDriver.exe).Hash) { throw 'Updated payload mismatch' }
if ([IO.File]::ReadAllText($target + '.previous') -cne 'old-version-sentinel') { throw 'Recovery backup missing' }
$process = Start-Process (Join-Path $root CosmoDriver.Update.exe) -ArgumentList '--apply-update',$planPath -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -or (Get-FileHash ($target + '.previous')).Hash -ne (Get-FileHash artifacts/CosmoDriver.exe).Hash) { throw 'Repeated update/recovery backup failed' }
# Tamper with the payload after the signature is generated. Target must remain unchanged.
[IO.File]::WriteAllText((Join-Path $package CosmoDriver.exe), 'tampered payload')
$before = (Get-FileHash $target).Hash
$process = Start-Process (Join-Path $root CosmoDriver.Update.exe) -ArgumentList '--apply-update',$planPath -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -eq 0 -or (Get-FileHash $target).Hash -ne $before) { throw 'Tampered auto-update was accepted or changed the target' }
Write-Output 'PASS signed auto-update replacement, recovery backup and tampered payload rejection'
[IO.File]::WriteAllText((Join-Path (Resolve-Path artifacts).Path 'auto-update-results.json'), '{"Success":true,"Checks":["signed replacement","recovery backup","repeated update","tampered payload rejected"]}')
