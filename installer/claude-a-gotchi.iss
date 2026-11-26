; Installer for claude-a-gotchi (Inno Setup 6). Built by tools/build.ps1:
;   iscc /DAppVersion=1.0.0 /DSourceDir=..\build\publish installer\claude-a-gotchi.iss
; Installs per user (no admin rights) into %LOCALAPPDATA%\Programs\claude-a-gotchi.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\publish"
#endif

[Setup]
AppId={{028C1319-EDE9-43D8-8269-028E36F29620}
AppName=claude-a-gotchi
AppVersion={#AppVersion}
AppVerName=claude-a-gotchi {#AppVersion}
AppPublisher=v1ncherry
AppPublisherURL=https://github.com/v1ncherry/claude-a-gotchi
AppSupportURL=https://github.com/v1ncherry/claude-a-gotchi/issues
AppUpdatesURL=https://github.com/v1ncherry/claude-a-gotchi/releases
DefaultDirName={autopf}\claude-a-gotchi
DefaultGroupName=claude-a-gotchi
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=claude-a-gotchi-{#AppVersion}-setup
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\claude-a-gotchi.exe
UninstallDisplayName=claude-a-gotchi
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.TaskHooks=Let Clawd react to Claude Code (adds hooks to Claude Code's settings.json and keeps a backup)
de.TaskHooks=Clawd auf Claude Code reagieren lassen (trägt Hooks in die settings.json von Claude Code ein, mit Sicherung)
en.TaskAutostart=Start with Windows
de.TaskAutostart=Mit Windows starten
en.TaskDesktop=Create a desktop shortcut
de.TaskDesktop=Verknüpfung auf dem Desktop anlegen
en.LaunchNow=Start claude-a-gotchi now
de.LaunchNow=claude-a-gotchi jetzt starten
en.RemoveData=Also delete your claude-a-gotchi settings and your own costumes?
de.RemoveData=Auch deine Einstellungen und eigenen Kostüme von claude-a-gotchi löschen?

[Tasks]
Name: "hooks"; Description: "{cm:TaskHooks}"
Name: "autostart"; Description: "{cm:TaskAutostart}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; files of an older version that are not part of this one
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\claude-a-gotchi"; Filename: "{app}\claude-a-gotchi.exe"
Name: "{autodesktop}\claude-a-gotchi"; Filename: "{app}\claude-a-gotchi.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--install-hooks"; Tasks: hooks; Flags: runhidden waituntilterminated
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--uninstall-hooks"; Tasks: not hooks; Flags: runhidden waituntilterminated
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--autostart on"; Tasks: autostart; Flags: runhidden waituntilterminated
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--autostart off"; Tasks: not autostart; Flags: runhidden waituntilterminated
Filename: "{app}\claude-a-gotchi.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--uninstall-hooks"; Flags: runhidden waituntilterminated; RunOnceId: "hooks"
Filename: "{app}\claude-a-gotchi.exe"; Parameters: "--autostart off"; Flags: runhidden waituntilterminated; RunOnceId: "autostart"

[Code]
// Clawd (and the old "Claudegotchi") must not be running while files are replaced.
procedure StopApp();
var
  R: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM claude-a-gotchi.exe', '', SW_HIDE, ewWaitUntilTerminated, R);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Claudegotchi.exe', '', SW_HIDE, ewWaitUntilTerminated, R);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopApp();
  Result := True;
end;

// Hooks and autostart are always removed. Settings and own costumes only after asking.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if (not UninstallSilent) and (MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\claude-a-gotchi'), True, True, True);
end;
