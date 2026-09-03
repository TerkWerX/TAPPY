; Tappy per-user installer scaffold.
; Compile only from a payload that already passed tools/Build-Portable.ps1.
; Example (after explicit installer-build authorization):
;   ISCC.exe /DAppVersion=0.1.0 /DPayloadDir="C:\absolute\audited\payload" Tappy.iss

#ifndef AppVersion
  #error AppVersion must be supplied as numeric major.minor.patch.
#endif

#ifndef PayloadDir
  #error PayloadDir must point to a freshly audited Tappy portable payload.
#endif

[Setup]
AppId={{B42E5FBB-E4AB-458A-908E-838C8BD101BB}
AppName=Tappy
AppVersion={#AppVersion}
AppVerName=Tappy {#AppVersion}
AppPublisher=TerkWerX
AppPublisherURL=https://www.terkwerx.com/tappy/
DefaultDirName={localappdata}\Programs\Tappy
DefaultGroupName=Tappy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\packages
OutputBaseFilename=Tappy-{#AppVersion}-Setup-x64
UninstallDisplayIcon={app}\Tappy.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\TerkWerX.Tappy.HandController.0_1
ChangesEnvironment=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\Tappy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\ControllerPacks\controller_registry.json"; DestDir: "{app}\ControllerPacks"; Flags: ignoreversion
Source: "{#PayloadDir}\ControllerPacks\trusted-publishers.json"; DestDir: "{app}\ControllerPacks"; Flags: ignoreversion

[Icons]
Name: "{group}\Tappy"; Filename: "{app}\Tappy.exe"
Name: "{autodesktop}\Tappy"; Filename: "{app}\Tappy.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Tappy.exe"; Description: "Launch Tappy"; Flags: nowait postinstall skipifsilent
