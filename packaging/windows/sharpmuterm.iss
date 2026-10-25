; Windows installer for SharpMUTerm: the desktop window, with the terminal client beside it so
; `sharpmuterm --window` finds the window executable. Built by release.yml:
;   iscc /DAppVersion=1.2.3 /DNumericVersion=1.2.3.0 /DPublishDir=<dir> /DOutputDir=<dir> sharpmuterm.iss

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
AppId={{6F1E2C1A-4B7D-4F8E-9C55-2A7B3D9E8F10}
AppName=SharpMUTerm
AppVersion={#AppVersion}
AppVerName=SharpMUTerm {#AppVersion}
AppPublisher=Harry Cordewener
AppPublisherURL=https://github.com/SharpMUSH/SharpMUTerm
AppSupportURL=https://github.com/SharpMUSH/SharpMUTerm/issues
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\SharpMUTerm
DefaultGroupName=SharpMUTerm
DisableProgramGroupPage=yes
; Per-user by default, so no UAC prompt; the first page offers an install for everyone instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
LicenseFile=..\..\LICENSE
SetupIconFile=..\icons\sharpmuterm.ico
UninstallDisplayIcon={app}\sharpmuterm-window.exe
OutputDir={#OutputDir}
OutputBaseFilename=SharpMUTerm-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add the install folder to PATH, so sharpmuterm runs from any terminal"; GroupDescription: "Terminal:"

[Files]
Source: "{#PublishDir}\sharpmuterm-window.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\sharpmuterm.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SharpMUTerm"; Filename: "{app}\sharpmuterm-window.exe"
Name: "{autodesktop}\SharpMUTerm"; Filename: "{app}\sharpmuterm-window.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\sharpmuterm-window.exe"; Description: "{cm:LaunchProgram,SharpMUTerm}"; Flags: nowait postinstall skipifsilent

[Code]
const
  SystemEnvironment = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironment = 'Environment';

function PathRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKEY_LOCAL_MACHINE else Result := HKEY_CURRENT_USER;
end;

function PathKey: String;
begin
  if IsAdminInstallMode then Result := SystemEnvironment else Result := UserEnvironment;
end;

{ Whether Dir is one of the ;-separated entries of Path, ignoring case and a trailing backslash. }
function PathHas(Path, Dir: String): Boolean;
begin
  Result := Pos(';' + Uppercase(RemoveBackslashUnlessRoot(Dir)) + ';',
                ';' + Uppercase(Path) + ';') > 0;
end;

{ Set when this installation put the folder on PATH, so uninstall never removes an entry the user made. }
const
  OwnKey = 'Software\SharpMUTerm';
  AddedToPath = 'AddedToPath';

procedure AddToPath(Dir: String);
var
  Path: String;
begin
  if not RegQueryStringValue(PathRoot, PathKey, 'Path', Path) then Path := '';
  if PathHas(Path, Dir) then Exit;
  if (Path <> '') and (Copy(Path, Length(Path), 1) <> ';') then Path := Path + ';';
  if RegWriteExpandStringValue(PathRoot, PathKey, 'Path', Path + RemoveBackslashUnlessRoot(Dir)) then
    RegWriteDWordValue(PathRoot, OwnKey, AddedToPath, 1);
end;

procedure RemoveFromPath(Dir: String);
var
  Path, Entry, Kept: String;
  P: Integer;
begin
  if not RegQueryStringValue(PathRoot, PathKey, 'Path', Path) then Exit;
  Kept := '';
  Path := Path + ';';
  while Path <> '' do
  begin
    P := Pos(';', Path);
    Entry := Copy(Path, 1, P - 1);
    Delete(Path, 1, P);
    if (Entry <> '') and (Uppercase(RemoveBackslashUnlessRoot(Entry)) <> Uppercase(RemoveBackslashUnlessRoot(Dir))) then
    begin
      if Kept <> '' then Kept := Kept + ';';
      Kept := Kept + Entry;
    end;
  end;
  RegWriteExpandStringValue(PathRoot, PathKey, 'Path', Kept);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Added: Cardinal;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if RegQueryDWordValue(PathRoot, OwnKey, AddedToPath, Added) and (Added = 1) then
    RemoveFromPath(ExpandConstant('{app}'));
  RegDeleteKeyIncludingSubkeys(PathRoot, OwnKey);
end;
