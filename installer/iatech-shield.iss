; ============================================================================
;  IATECH-SHIELD PRO — script d'installateur Inno Setup
;  Génère un .exe d'installation pour Windows.
;  Compilation : iscc installer\iatech-shield.iss
;  Prérequis   : avoir d'abord publié l'exécutable autonome (voir README).
; ============================================================================

#define MyAppName "IATECH-SHIELD PRO"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "IATECH"
#define MyAppExeName "iatech-shield.exe"

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
; Installation pour tous les utilisateurs -> nécessite des droits administrateur.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "addtopath"; Description: "Ajouter iatech-shield au PATH (utilisation en ligne de commande)"; GroupDescription: "Options :"

[Files]
; On embarque tout le contenu du dossier de publication autonome.
Source: "..\src\IatechShield\bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Ajoute le dossier d'installation au PATH système si la tâche est cochée.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath('{app}'); Tasks: addtopath

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "version"; Description: "Vérifier l'installation"; Flags: postinstall skipifsilent runascurrentuser

[Code]
// Évite d'ajouter deux fois le même dossier au PATH.
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
