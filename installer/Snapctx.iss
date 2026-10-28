; Snapctx for Windows: a per-user installer (no administrator rights) for the self-contained x64 build.
; It installs to %LOCALAPPDATA%\Programs\Snapctx, adds Start Menu (and optionally desktop) shortcuts,
; asks whether Snapctx starts at sign-in, and starts it when setup finishes. Uninstall removes the program
; and its startup shortcut; settings in %APPDATA%\Snapctx and logs in %LOCALAPPDATA%\Snapctx are kept.
;
; Build (after: dotnet publish src/ContextSnap -c Release -r win-x64 --self-contained -o publish/installer-app):
;   ISCC.exe /DAppVersion=1.1.0 /DSourceDir=..\publish\installer-app /DOutputDir=..\publish installer\Snapctx.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\installer-app"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

[Setup]
; Never change AppId: upgrades and the uninstaller find earlier installs by it.
AppId={{7E0C9A52-4B8D-4F6B-9C1E-5A3D2B8F6E41}
AppName=Snapctx
AppVersion={#AppVersion}
AppVerName=Snapctx {#AppVersion}
AppPublisher=DarkLordGeo
AppPublisherURL=https://github.com/DarkLordGeo/ContextSnap
AppSupportURL=https://github.com/DarkLordGeo/ContextSnap/issues
AppComments=Your desktop context. One shortcut. Ready for AI.
DefaultDirName={localappdata}\Programs\Snapctx
DefaultGroupName=Snapctx
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Snapctx-Setup
SetupIconFile=..\src\ContextSnap\ContextSnap.ico
UninstallDisplayIcon={app}\Snapctx.exe
UninstallDisplayName=Snapctx
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Snapctx

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Snapctx in the background when I sign in, so Ctrl+Alt+Shift+C always works"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Snapctx"; Filename: "{app}\Snapctx.exe"; Comment: "Copy your desktop context as Markdown"
Name: "{autodesktop}\Snapctx"; Filename: "{app}\Snapctx.exe"; Tasks: desktopicon

[Run]
; The app records the choice in its settings and adds or removes its own Startup-folder shortcut
; (Snapctx.lnk); it also removes a ContextSnap.lnk left by the ZIP build from before the rename.
Filename: "{app}\Snapctx.exe"; Parameters: "--enable-startup"; Tasks: startup; Flags: runhidden waituntilterminated
Filename: "{app}\Snapctx.exe"; Parameters: "--disable-startup"; Tasks: not startup; Flags: runhidden waituntilterminated
Filename: "{app}\Snapctx.exe"; Description: "Start Snapctx now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\Snapctx.exe"; Parameters: "--exit"; RunOnceId: "StopSnapctx"; Flags: runhidden waituntilterminated

[UninstallDelete]
; Only the startup shortcut this app creates. Settings and logs stay unless the user deletes them.
Type: files; Name: "{userstartup}\Snapctx.lnk"

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  // Shared by Snapctx and ContextSnap (its earlier name), so either one is found and stopped.
  InstanceMutex = 'Local\ContextSnap.Instance';
  ExitEvent = 'Local\ContextSnap.Exit';

function OpenEvent(DesiredAccess: Cardinal; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

// Asks a running Snapctx or ContextSnap to quit (it unregisters the shortcut and exits) and waits up to
// about five seconds. Returns False if it is still running.
function StopRunningInstance(): Boolean;
var
  Handle: THandle;
  Tries: Integer;
begin
  Result := True;
  if not CheckForMutexes(InstanceMutex) then
    Exit;
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, ExitEvent);
  if Handle <> 0 then
  begin
    SetEvent(Handle);
    CloseHandle(Handle);
  end;
  Tries := 0;
  while CheckForMutexes(InstanceMutex) and (Tries < 25) do
  begin
    Sleep(200);
    Tries := Tries + 1;
  end;
  Result := not CheckForMutexes(InstanceMutex);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopRunningInstance() then
    Result := 'Snapctx (or ContextSnap) is still running. Quit it from its window or the notification-area icon, then run setup again.';
end;
