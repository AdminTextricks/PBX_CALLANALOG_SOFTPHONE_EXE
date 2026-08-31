; CallAnalog Softphone — Inno Setup 6 installer
; Packages the self-contained win-x64 publish output.
; Keep #define MyAppVersion in sync with VERSION at the repo root.
;
; Compile (after publishing):
;   "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\CallAnalogSoftphone.iss
; Output:
;   installer\output\CallAnalogSoftphone-Setup-<version>.exe
;
; Signing: uncomment SignTool / SignedUninstaller after a code-signing
; certificate is installed. See docs/CODE_SIGNING.md.

#define MyAppName "CallAnalog Softphone"
#define MyAppVersion "2.2.7"
#define MyAppPublisher "CallAnalog"
#define MyAppExeName "CallAnalog.Softphone.exe"
#define MyAppMutex "Global\CallAnalog.Softphone.SingleInstance"

; SDK publish folder for TargetFramework net10.0-windows10.0.18362, RID win-x64.
; This is a multi-file self-contained layout (exe + native WPF runtime DLLs +
; appsettings.json + Assets), not PublishSingleFile.
#define PublishDir "..\bin\Release\net10.0-windows10.0.18362\win-x64\publish"

; Stable AppId so upgrades replace the previous install instead of duplicating
; Add/Remove Programs and Start Menu entries.
#define MyAppId "{{8F3C1A72-6D4E-4B9A-9C21-2E7F5A91B0C4}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) {#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (C) {#MyAppPublisher}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362

OutputDir=output
OutputBaseFilename=CallAnalogSoftphone-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

UsePreviousAppDir=yes
UsePreviousGroup=yes
CloseApplications=yes
RestartApplications=no
AppMutex={#MyAppMutex}

; User settings, logs, recordings, and credentials live under
; %LOCALAPPDATA%\CallAnalog (not {app}). This installer never deletes that folder.

; SignTool=signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a $f
; SignedUninstaller=yes

#if FileExists("..\CallAnalog.Softphone.ico")
SetupIconFile=..\CallAnalog.Softphone.ico
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Include the full publish tree. WPF self-contained output requires the
; accompanying *_cor3.dll files, appsettings.json, and Assets (when present).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "{#MyAppName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "{#MyAppName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
