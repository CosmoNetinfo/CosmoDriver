#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
[Setup]
AppId={{C74D0BAE-1854-4B7E-91CE-935AC9EC44F5}
AppName=CosmoDriver
AppVersion={#AppVersion}
AppPublisher=CosmoNet
AppPublisherURL=https://github.com/CosmoNetinfo/CosmoDriver
DefaultDirName={localappdata}\Programs\CosmoDriver
DefaultGroupName=CosmoDriver
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=CosmoDriver-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\CosmoDriver\Assets\CosmoDriver.ico
UninstallDisplayIcon={app}\CosmoDriver.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
LicenseFile=..\LICENSE
[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "..\artifacts\publish\CosmoDriver.exe"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\CosmoDriver"; Filename: "{app}\CosmoDriver.exe"
Name: "{autodesktop}\CosmoDriver"; Filename: "{app}\CosmoDriver.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Crea un collegamento sul desktop"; Flags: unchecked
[InstallDelete]
Type: files; Name: "{app}\CosmoDriver.exe.previous"
[UninstallDelete]
Type: files; Name: "{app}\CosmoDriver.exe.previous"
[Run]
Filename: "{app}\CosmoDriver.exe"; Description: "Avvia CosmoDriver"; Flags: nowait postinstall skipifsilent
