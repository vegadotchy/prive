; ============================================================================
;  IATECH-SHIELD PRO — script d'installateur Inno Setup
;  Génère un .exe d'installation pour Windows (interface graphique + CLI).
;  Compilation : iscc installer\iatech-shield.iss
;  Prérequis   : avoir d'abord publié les exécutables (voir build-installer.bat).
; ============================================================================

#define MyAppName "IATECH-SHIELD PRO"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "IATECH"
#define MyGuiExe "iatech-shield-gui.exe"
#define MyCliExe "iatech-shield.exe"

[Setup]
AppId={{8E2A6C41-7F3D-4B92-9A1C-IATECHSHIELD01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
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

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath('{app}'); Tasks: addtopath

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
