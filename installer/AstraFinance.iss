; Astra Finance — Windows installer (Inno Setup 6)
;
; Per-user installation: no administrator rights, installs to %LOCALAPPDATA%\Programs\Astra Finance.
; Start Menu shortcut, optional Desktop shortcut, Windows uninstaller (Settings → Apps).
; The uninstaller KEEPS the user's data (database, backups, settings, logs in %LOCALAPPDATA%\AstraFinance)
; unless the user explicitly chooses to delete it. Silent uninstalls never delete data unless
; /DELETEUSERDATA is passed on the command line.
;
; Build:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\AstraFinance.iss
; Input:  artifacts\publish  (dotnet publish -c Release -r win-x64 --self-contained true)
; Output: artifacts\installer\AstraFinance-Setup-<version>.exe

#define AppName "Astra Finance"
#define AppVersion "1.0.0"
#define AppPublisher "Aprotoos"
#define AppExe "AstraFinance.exe"
#define AppUserModelId "Aprotoos.AstraFinance"
#define DataFolderName "AstraFinance"

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{6E1B2C5A-7F43-4D8E-9A21-3C5F0B8D7E14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=AstraFinance-Setup-{#AppVersion}
SetupIconFile=..\src\AstraFinance.App\Resources\astra.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running Astra Finance (it holds this mutex) before installing or uninstalling.
AppMutex=Local\AstraFinance.SingleInstance.7C3E1A52
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; The shortcut carries the same AppUserModelID as the app, so Windows notifications show as Astra Finance.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Comment: "Personal finance manager"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Registry]
; Notification display name for this user; removed again on uninstall.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#AppName}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Personal data lives outside the installation folder and is kept by default.
function DataFolder(): String;
begin
  Result := ExpandConstant('{localappdata}\{#DataFolderName}');
end;

function WantsDataDeleted(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/DELETEUSERDATA') = 0 then
    begin
      Result := True;
      Exit;
    end;

  if UninstallSilent then
    Exit; // silent uninstall without /DELETEUSERDATA: always keep the data

  Result := MsgBox(
    'Astra Finance has been removed.' + #13#10 + #13#10 +
    'Your data is still on this computer:' + #13#10 + DataFolder() + #13#10 +
    '(database with all financial records, settings, backups and logs)' + #13#10 + #13#10 +
    'Do you also want to permanently DELETE this data?' + #13#10 +
    'Choose "No" to keep it (recommended) — reinstalling Astra Finance will then pick it up again.' + #13#10 +
    'Backups saved in other folders you chose are never deleted.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

  if Result then
    Result := MsgBox(
      'This permanently deletes all Astra Finance data in' + #13#10 + DataFolder() + #13#10 + #13#10 +
      'It cannot be undone. Delete it now?',
      mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DirExists(DataFolder()) then
  begin
    if WantsDataDeleted() then
    begin
      if not DelTree(DataFolder(), True, True, True) then
        if not UninstallSilent then
          MsgBox('Some files could not be deleted. You can remove the folder manually:' + #13#10 + DataFolder(), mbInformation, MB_OK);
    end;
  end;
end;
