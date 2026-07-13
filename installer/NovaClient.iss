; Nova Client — Inno Setup script
; Build:  ISCC.exe installer\NovaClient.iss
; Input:  dist\single\  (single-file publish — see docs/installer-release.md)
; Output: dist\installer\NovaClientSetup-<version>.exe

#define AppName "Nova Client"
#define AppVersion "1.0.0"
#define AppPublisher "Nova Client"
#define AppExeName "NovaClient.exe"
#define AppUrl "https://example.com"

[Setup]
AppId={{8B3F62D4-9C1E-4A57-B7F0-1AC29E6C4D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/support
VersionInfoVersion={#AppVersion}
; Per-user install: no administrator rights required, no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=NovaClientSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
LicenseFile=..\docs\THIRD-PARTY-NOTICES.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\single\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: no Minecraft or OptiFine files are packaged — the launcher downloads official Minecraft
; files at first run and the user supplies their own OptiFine jar (see docs).

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the application folder — never user data without asking (see [Code]).
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\NovaClient');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete your Nova Client data (settings, game files, screenshots, logs) in'#13#10
                + DataDir + '?', mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
