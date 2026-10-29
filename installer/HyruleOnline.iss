; Thin per-user installer: only the launcher, the input router and the scripts.
; Third-party components are downloaded by the first-run setup, never bundled.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; Same limit as src\Launcher\PathGuard.cs and scripts\Path-Guard.ps1: 260 - 1 - 166 (deepest known file under the folder) - 10 (headroom).
; BCML, Cemu and the Milk Bar launcher break on longer paths. A test keeps the three numbers equal.
#define MaxRootLength 83
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
SetupIconFile=..\assets\icon.ico
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

[Code]
function IsInside(Dir, Parent: String): Boolean;
begin
  Parent := RemoveBackslashUnlessRoot(Parent);
  Result := (Parent <> '') and ((CompareText(Dir, Parent) = 0) or (CompareText(Copy(Dir, 1, Length(Parent) + 1), Parent + '\') = 0));
end;

// Why the chosen folder will probably cause trouble, or an empty string when it is fine.
function FolderProblem(Dir: String): String;
begin
  Result := '';
  Dir := RemoveBackslashUnlessRoot(Dir);
  if Length(Dir) > {#MaxRootLength} then
    Result := 'The folder path is ' + IntToStr(Length(Dir)) + ' characters long and the limit is {#MaxRootLength}. BCML, Cemu and the Milk Bar launcher fail on long paths.'
  else if Copy(Dir, 1, 2) = '\\' then
    Result := 'The folder is on a network location. The game and its tools need a local drive.'
  else if IsInside(Dir, ExpandConstant('{commonpf}')) or IsInside(Dir, ExpandConstant('{commonpf32}')) then
    Result := 'The folder is under Program Files, which Windows protects from changes by regular programs.'
  else if (Pos('\onedrive', Lowercase(Dir)) > 0) or IsInside(Dir, GetEnv('OneDrive')) then
    Result := 'The folder is synced by OneDrive, which can lock or rewrite files while the game uses them.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Problem: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Problem := FolderProblem(WizardDirValue);
    if Problem <> '' then
      Result := SuppressibleMsgBox(Problem + #13#10#13#10 + 'Choose a short local folder such as C:\HyruleOnline instead. Install here anyway?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) = IDYES;
  end;
end;
