; NightDisplay installer (Inno Setup 6)
; Build with:  ./scripts/build-installer.ps1
; The release folder must already contain NightDisplay.exe and NightDisplay.Guard.exe.

#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef AppName
  #define AppName "NightDisplay"
#endif

#define AppPublisher "NightDisplay contributors"
#define AppUrl "https://github.com/wzp100/NightDisplay"
#define ProjectRoot AddBackslash(SourcePath) + ".."
#define ReleaseDir ProjectRoot + "\artifacts\release"

[Setup]
AppId={{6E2B0B71-6C90-4C57-9E1B-9E0A5C2D7F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#ProjectRoot}\artifacts
OutputBaseFilename={#AppName}-{#AppVersion}-setup-x64
SetupIconFile={#ProjectRoot}\assets\icons\nightdisplay.ico
UninstallDisplayIcon={app}\NightDisplay.exe
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile={#ProjectRoot}\LICENSE
DisableReadyMemo=no
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Chinese translations are not bundled with Inno Setup. Drop ChineseSimplified.isl into
; the Languages folder (https://github.com/jrsoftware/issrc) and enable this line:
; Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "installdriver"; Description: "安装虚拟显示器驱动（需要重启前确认）"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\NightDisplay.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\NightDisplay.Guard.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\driver\*"; DestDir: "{app}\driver"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\NightDisplay.exe"
Name: "{group}\使用说明"; Filename: "{app}\README.md"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\NightDisplay.exe"; Tasks: desktopicon

[Run]
; runasoriginaluser: the background process and tray must live in the logged-on user's session,
; not the elevated installer's session.
Filename: "{app}\NightDisplay.exe"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\NightDisplay.exe"; Parameters: "--install-driver"; Description: "安装虚拟显示器驱动"; Flags: nowait runasoriginaluser; Tasks: installdriver

[UninstallRun]
; Restore the physical display before files are removed. --quit restores, then exits the background.
Filename: "{app}\NightDisplay.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitBackground"
Filename: "{app}\NightDisplay.exe"; Parameters: "--restore --headless"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreDisplay"
; Remove the per-user logon entry the app creates (it lives in the uninstalling user's hive).
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v NightDisplay /f"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveLogonEntry"

[Code]
const
  LEASE_MUTEX_GRACE_MS = 4000;

procedure StopRunningBackground;
var
  ResultCode: Integer;
begin
  if not FileExists(ExpandConstant('{app}\NightDisplay.exe')) then
    Exit;
  // Ask the app to restore the physical screen and exit. Give the display switch time
  // to finish before files are replaced or deleted.
  Exec(ExpandConstant('{app}\NightDisplay.exe'), '--quit',
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(LEASE_MUTEX_GRACE_MS);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningBackground;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunningBackground;
end;
