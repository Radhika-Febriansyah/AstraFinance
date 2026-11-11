; Astra Finance — Windows installer (Inno Setup 6.7+)
;
; Per-user installation: no administrator rights, installs to %LOCALAPPDATA%\Programs\Astra Finance.
; Start Menu shortcut, optional Desktop shortcut, Windows uninstaller (Settings → Apps).
; The uninstaller KEEPS the user's data (database, backups, settings, logs in %LOCALAPPDATA%\AstraFinance)
; unless the user explicitly chooses to delete it (two confirmations, "No" is the default). Silent uninstalls
; never delete data unless /DELETEUSERDATA is passed on the command line.
;
; Branding (1.1): "Minimalist Corporate" banner — Deep Navy, blue-gradient symbol left, white wordmark right,
; no tagline — on every inner Setup page and in the uninstaller. Light/dark follows Windows (modern dynamic).
; Languages: English and Indonesian. A first installation follows the Windows display language; an upgrade
; follows the language chosen in the app (the app updates the language Setup remembers for this installation).
;
; Build:  installer\build-release.ps1 (or "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\AstraFinance.iss)
; Input:  artifacts\publish  (dotnet publish -c Release -r win-x64 --self-contained true)
; Output: artifacts\installer\AstraFinance-Setup-<version>.exe

#define AppName "Astra Finance"
#define AppPublisher "Aprotoos"
#define AppExe "AstraFinance.exe"
#define AppUserModelId "Aprotoos.AstraFinance"
#define DataFolderName "AstraFinance"

; The version comes from the published app (Directory.Build.props), so Setup can never disagree with it.
#define PublishedExe "..\artifacts\publish\" + AppExe
#if !FileExists(PublishedExe)
  #error Publish the app first (installer\build-release.ps1): artifacts\publish\AstraFinance.exe was not found.
#endif
#define FullVersion GetStringFileInfo(PublishedExe, "ProductVersion")
#if Pos("+", FullVersion) > 0
  #define AppVersion Copy(FullVersion, 1, Pos("+", FullVersion) - 1)
#else
  #define AppVersion FullVersion
#endif

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
; Light or dark like Windows. The brand banner replaces the small header image (see [Code]).
WizardStyle=modern dynamic
WizardImageFile=branding\wizard-image-100.bmp,branding\wizard-image-125.bmp,branding\wizard-image-150.bmp,branding\wizard-image-175.bmp,branding\wizard-image-200.bmp,branding\wizard-image-225.bmp,branding\wizard-image-250.bmp
WizardSmallImageFile=branding\wizard-small-100.bmp,branding\wizard-small-125.bmp,branding\wizard-small-150.bmp,branding\wizard-small-175.bmp,branding\wizard-small-200.bmp,branding\wizard-small-225.bmp,branding\wizard-small-250.bmp
; The brand images are Deep Navy, so dark mode uses the same ones (otherwise Inno shows its own dark images).
WizardImageFileDynamicDark=branding\wizard-image-100.bmp,branding\wizard-image-125.bmp,branding\wizard-image-150.bmp,branding\wizard-image-175.bmp,branding\wizard-image-200.bmp,branding\wizard-image-225.bmp,branding\wizard-image-250.bmp
WizardSmallImageFileDynamicDark=branding\wizard-small-100.bmp,branding\wizard-small-125.bmp,branding\wizard-small-150.bmp,branding\wizard-small-175.bmp,branding\wizard-small-200.bmp,branding\wizard-small-225.bmp,branding\wizard-small-250.bmp
; First installation: the Windows display language (Indonesian or English). Upgrades: the language remembered
; for this installation, which the app keeps in step with the language chosen in the app (InstallerLanguageSync).
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=yes
ShowLanguageDialog=no
; Close a running Astra Finance (it holds this mutex) before installing or uninstalling.
AppMutex=Local\AstraFinance.SingleInstance.7C3E1A52
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "indonesian"; MessagesFile: "Languages\Indonesian.isl"

[CustomMessages]
english.DesktopShortcut=Create a &Desktop shortcut
indonesian.DesktopShortcut=Buat pintasan di &Desktop
english.AdditionalShortcuts=Additional shortcuts:
indonesian.AdditionalShortcuts=Pintasan tambahan:
english.StartApp=Start {#AppName}
indonesian.StartApp=Jalankan {#AppName}
english.ShortcutComment=Personal finance manager
indonesian.ShortcutComment=Pengelola keuangan pribadi
english.KeepDataQuestion={#AppName} has been removed.%n%nYour data is still on this computer:%n%1%n(database with all financial records, settings, backups and logs)%n%nDo you also want to permanently DELETE this data?%nChoose "No" to keep it (recommended) — reinstalling {#AppName} will then pick it up again.%nBackups saved in other folders you chose are never deleted.
indonesian.KeepDataQuestion={#AppName} telah dihapus.%n%nData Anda masih ada di komputer ini:%n%1%n(database berisi semua catatan keuangan, pengaturan, cadangan, dan log)%n%nApakah Anda juga ingin MENGHAPUS data ini secara permanen?%nPilih "Tidak" untuk menyimpannya (disarankan) — jika {#AppName} dipasang lagi, data ini akan digunakan kembali.%nCadangan yang disimpan di folder lain pilihan Anda tidak pernah dihapus.
english.DeleteDataConfirm=This permanently deletes all {#AppName} data in%n%1%n%nIt cannot be undone. Delete it now?
indonesian.DeleteDataConfirm=Tindakan ini menghapus semua data {#AppName} di%n%1%nsecara permanen.%n%nTindakan ini tidak dapat dibatalkan. Hapus sekarang?
english.DeleteDataFailed=Some files could not be deleted. You can remove the folder manually:%n%1
indonesian.DeleteDataFailed=Beberapa berkas tidak dapat dihapus. Anda dapat menghapus folder ini secara manual:%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalShortcuts}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
; Brand banner: extracted for Setup itself, and installed so the uninstaller can show it too.
Source: "branding\header-banner.png"; Flags: dontcopy
Source: "branding\header-banner.png"; DestDir: "{app}\branding"; Flags: ignoreversion

[Icons]
; The shortcut carries the same AppUserModelID as the app, so Windows notifications show as Astra Finance.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{cm:ShortcutComment}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{cm:ShortcutComment}"; Tasks: desktopicon

[Registry]
; Notification display name for this user; removed again on uninstall.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#AppName}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:StartApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  BrandNavy = $542517;        // #172554 (TColor is BGR)
  BannerTitleColor = $FFFFFF;
  BannerTextColor = $EAC3B4;  // #B4C3EA
  BannerAspectNum = 536;      // header-banner.png is 536 x 200
  BannerAspectDen = 200;

// ---------------------------------------------------------------- data folder (kept by default)
function DataFolder(): String;
begin
  Result := ExpandConstant('{localappdata}\{#DataFolderName}');
end;

// ---------------------------------------------------------------- brand banner (Setup and uninstaller)
procedure ApplyBanner(Panel: TPanel; Title, Description: TNewStaticText; SmallImage: TBitmapImage; const PngFile: String);
var
  Fill, Logo: TBitmapImage;
  LogoWidth: Integer;
begin
  if not FileExists(PngFile) then
    Exit;

  // Deep Navy behind everything in the header.
  Fill := TBitmapImage.Create(Panel);
  Fill.Parent := Panel;
  Fill.Align := alClient;
  Fill.BackColor := BrandNavy;

  // Symbol + wordmark, scaled to the header height (aspect ratio kept).
  Logo := TBitmapImage.Create(Panel);
  Logo.Parent := Panel;
  Logo.Stretch := True;
  Logo.BackColor := BrandNavy;
  Logo.PngImage.LoadFromFile(PngFile);
  LogoWidth := Panel.ClientHeight * BannerAspectNum div BannerAspectDen;
  Logo.SetBounds(0, 0, LogoWidth, Panel.ClientHeight);

  if SmallImage <> nil then
    SmallImage.Visible := False;

  // Page title and description stay readable, in white, beside the brand.
  Title.Font.Color := BannerTitleColor;
  Title.Left := LogoWidth + ScaleX(4);
  Title.Width := Panel.ClientWidth - Title.Left - ScaleX(16);
  Title.Anchors := [akLeft, akTop, akRight];
  Title.BringToFront;
  Description.Font.Color := BannerTextColor;
  Description.Left := Title.Left + ScaleX(8);
  Description.Width := Panel.ClientWidth - Description.Left - ScaleX(16);
  Description.Anchors := [akLeft, akTop, akRight];
  Description.BringToFront;
end;

procedure InitializeWizard();
begin
  ExtractTemporaryFile('header-banner.png');
  ApplyBanner(WizardForm.MainPanel, WizardForm.PageNameLabel, WizardForm.PageDescriptionLabel,
    WizardForm.WizardSmallBitmapImage, ExpandConstant('{tmp}\header-banner.png'));
end;

procedure InitializeUninstallProgressForm();
begin
  ApplyBanner(UninstallProgressForm.MainPanel, UninstallProgressForm.PageNameLabel, UninstallProgressForm.PageDescriptionLabel,
    UninstallProgressForm.WizardSmallBitmapImage, ExpandConstant('{app}\branding\header-banner.png'));
end;

// ---------------------------------------------------------------- uninstall: personal data is kept by default
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

  Result := MsgBox(FmtMessage(CustomMessage('KeepDataQuestion'), [DataFolder()]),
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

  if Result then
    Result := MsgBox(FmtMessage(CustomMessage('DeleteDataConfirm'), [DataFolder()]),
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
          MsgBox(FmtMessage(CustomMessage('DeleteDataFailed'), [DataFolder()]), mbInformation, MB_OK);
    end;
  end;
end;
