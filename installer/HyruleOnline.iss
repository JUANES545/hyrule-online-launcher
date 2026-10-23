; Thin per-user installer: only the launcher, the input router and the scripts.
; Third-party components are downloaded by the first-run setup, never bundled.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point at the staged package folder.
#endif

[Setup]
AppId={{6D6F3F0A-5C1B-4B55-9E4E-1B6F0C2A7E11}
AppName=Hyrule Online
AppVersion={#AppVersion}
AppPublisher=JUANES545
DefaultDirName=C:\HyruleOnline
DefaultGroupName=Hyrule Online
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\.artifacts\app\HyruleOnline.exe
OutputBaseFilename=HyruleOnline-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Hyrule Online"; Filename: "{app}\.artifacts\app\HyruleOnline.exe"; WorkingDir: "{app}\.artifacts\app"
Name: "{autodesktop}\Hyrule Online"; Filename: "{app}\.artifacts\app\HyruleOnline.exe"; WorkingDir: "{app}\.artifacts\app"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\.artifacts\app\HyruleOnline.exe"; Description: "Start Hyrule Online"; Flags: nowait postinstall skipifsilent
