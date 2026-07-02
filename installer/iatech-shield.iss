; ============================================================================
;  IATECH-SHIELD PRO — script d'installateur Inno Setup
;  Génère un .exe d'installation pour Windows (interface graphique + CLI).
;  Compilation : iscc installer\iatech-shield.iss
;  Prérequis   : avoir d'abord publié les exécutables (voir build-installer.bat).
; ============================================================================

#define MyAppName "IATECH-SHIELD PRO"
#define MyAppVersion "0.16.6"
#define MyAppPublisher "IATECHFUTUR"
#define MyAppUrl "https://github.com/vegadotchy/prive"
#define MyGuiExe "iatech-shield-gui.exe"
#define MyCliExe "iatech-shield.exe"

[Setup]
AppId={{8E2A6C41-7F3D-4B92-9A1C-IATECHSHIELD01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}/releases
; Logo bouclier dans « Programmes et fonctionnalités » + désinstallation propre.
UninstallDisplayIcon={app}\{#MyGuiExe}
UninstallDisplayName={#MyAppName}
SetupIconFile=..\src\IatechShield.Gui\icon.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=IATECH-SHIELD PRO — Antivirus
DefaultDirName={autopf}\IatechShield
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=IatechShield-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Habillage « bouclier » de l'assistant + dialogue de choix de langue.
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
ShowLanguageDialog=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
Name: "addtopath"; Description: "Ajouter la commande iatech-shield au PATH"; GroupDescription: "Options :"

[Files]
; Dossier de publication (contient l'interface, la CLI et signatures.json).
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Extension navigateur (enregistrement automatique des identifiants).
Source: "..\browser-extension\*"; DestDir: "{app}\browser-extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath('{app}'); Tasks: addtopath

; --- Menu contextuel (clic droit) « Scanner avec IATECHSHIELD PRO » ---------
; Fichiers
Root: HKCR; Subkey: "*\shell\IatechShieldScan"; ValueType: string; ValueName: ""; ValueData: "Scanner avec IATECHSHIELD PRO"; Flags: uninsdeletekey
Root: HKCR; Subkey: "*\shell\IatechShieldScan"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyGuiExe},0"
Root: HKCR; Subkey: "*\shell\IatechShieldScan\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyGuiExe}"" --scan ""%1"""
; Dossiers
Root: HKCR; Subkey: "Directory\shell\IatechShieldScan"; ValueType: string; ValueName: ""; ValueData: "Scanner avec IATECHSHIELD PRO"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\IatechShieldScan"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyGuiExe},0"
Root: HKCR; Subkey: "Directory\shell\IatechShieldScan\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyGuiExe}"" --scan ""%1"""
; Arrière-plan d'un dossier ouvert
Root: HKCR; Subkey: "Directory\Background\shell\IatechShieldScan"; ValueType: string; ValueName: ""; ValueData: "Scanner avec IATECHSHIELD PRO"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\IatechShieldScan"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyGuiExe},0"
Root: HKCR; Subkey: "Directory\Background\shell\IatechShieldScan\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyGuiExe}"" --scan ""%V"""
; Disques durs, partitions et clés USB / disques externes
Root: HKCR; Subkey: "Drive\shell\IatechShieldScan"; ValueType: string; ValueName: ""; ValueData: "Scanner avec IATECHSHIELD PRO"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Drive\shell\IatechShieldScan"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyGuiExe},0"
Root: HKCR; Subkey: "Drive\shell\IatechShieldScan\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyGuiExe}"" --scan ""%1"""

[Run]
Filename: "{app}\{#MyGuiExe}"; Description: "Lancer {#MyAppName}"; Flags: postinstall skipifsilent nowait runascurrentuser

[Code]
var
  ProgressPercent: TNewStaticText;

procedure InitializeWizard;
begin
  { Étiquette de pourcentage sous la barre de progression. }
  ProgressPercent := TNewStaticText.Create(WizardForm);
  ProgressPercent.Parent := WizardForm.InstallingPage;
  ProgressPercent.AutoSize := True;
  ProgressPercent.Left := WizardForm.ProgressGauge.Left;
  ProgressPercent.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(8);
  ProgressPercent.Font.Style := [fsBold];
  ProgressPercent.Caption := '0 %';
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if (ProgressPercent <> nil) and (MaxProgress > 0) then
    ProgressPercent.Caption := IntToStr(Round(CurProgress * 100.0 / MaxProgress)) + ' %';
end;

{ Garde de désinstallation : exige une carte d'identité autorisée. L'application est
  lancée avec « --uninstall-auth » ; elle renvoie 0 si la carte autorise la
  désinstallation (et envoie un rapport à IATECHFUTUR), sinon un code non nul. }
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if Exec(ExpandConstant('{app}\{#MyGuiExe}'), '--uninstall-auth', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode <> 0 then
      Result := False;   { carte absente ou non autorisée : on annule la désinstallation }
  end;
  { Si l'exécutable est introuvable, on n'empêche pas la désinstallation (anti-blocage). }
end;

function NeedsAddPath(Param: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKLM,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + ExpandConstant(Param) + ';', ';' + OrigPath + ';') = 0;
end;
