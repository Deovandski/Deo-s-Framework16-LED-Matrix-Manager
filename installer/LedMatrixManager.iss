; Inno Setup 6 script for Deo's LED Matrix Manager.
; Build locally:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.1.0 installer\LedMatrixManager.iss
; (after: dotnet publish src\LedMatrix.App -c Release -o publish)

#define MyAppName "Deo's LED Matrix Manager"
#define MyAppExeName "DeoLedMatrixManager.exe"
#define MyAppPublisher "Deovandski"
#define MyAppUrl "https://deovandski.github.io/Deo-s-Framework16-LED-Matrix-Manager/"
#define MyRepoUrl "https://github.com/Deovandski/Deo-s-Framework16-LED-Matrix-Manager"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; Never change AppId: it identifies the app for upgrades and uninstall.
AppId={{8C4C2E2B-6D0B-4C5B-9A51-3E2F7B1F2A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyRepoUrl}/issues
AppUpdatesURL={#MyRepoUrl}/releases
; Per-user install into %LocalAppData%\Programs, no admin prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\DeoLedMatrixManager
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Ask to close the running app (same mutex name as SingleInstance.cs).
AppMutex=DeoLedMatrixManager.SingleInstance
CloseApplications=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\LedMatrix.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir=Output
OutputBaseFilename=DeoLedMatrixManager-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start automatically when I sign in to Windows (recommended)"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same value the app's "Start with Windows" checkbox writes (StartupRegistration.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeoLedMatrixManager"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app may have added the startup entry itself, so always remove it. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DeoLedMatrixManager');

    if (not UninstallSilent) and
       (MsgBox('Also delete your saved drawings, settings and logs?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      DelTree(ExpandConstant('{userappdata}\DeoLedMatrixManager'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\DeoLedMatrixManager'), True, True, True);
    end;
  end;
end;
