; Instalador de Windows (Inno Setup 6: https://jrsoftware.org/isinfo.php)
;
; Se genera con la tarea de VS Code "Instalador: Windows" (publica en dist\publish\win-x64 y llama a
; packaging\windows\build.ps1, que pasa la versión del .csproj). Resultado: dist\MdViewer-Setup-<versión>.exe
;
; Actualizaciones: ejecutar un instalador más nuevo sobre una instalación existente la actualiza en el mismo
; directorio (conserva accesos directos y opciones). La tarea sube sola el <Version> del .csproj (1.0 -> 1.1).

#define AppName "MD Viewer"
#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#define AppExe "MdViewer.exe"
; Tipo de archivo "Documento Markdown": da a los .md su propio icono (hoja + logo) cuando se abren con la app
#define ProgId "MdViewer.md"
#define PublishDir "..\..\dist\publish\win-x64"

[Setup]
; AppId identifica la app entre versiones: NO cambiarlo nunca o el instalador nuevo no reconocerá la instalación previa
AppId={{7C1E5B2A-4F3D-4B8E-9A61-2D5C8E0F3B47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
; Siempre el mismo modo (todos los usuarios, en Program Files): así cada versión encuentra y reemplaza a la anterior
PrivilegesRequired=admin
; Si la app está abierta al actualizar, se cierra antes de reemplazar los archivos
CloseApplications=yes
RestartApplications=no
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=MdViewer-Setup-{#AppVersion}
SetupIconFile=..\..\src\MdViewer\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Avisa al Explorador de Windows del cambio de asociaciones para que refresque los iconos de los .md
ChangesAssociations=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs
Source: "mdfile.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Tipo "Documento Markdown": nombre, icono de documento (mdfile.ico) y cómo abrirlo
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "Documento Markdown"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\mdfile.ico"""
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
; Ofrece MD Viewer para .md en "Abrir con" usando ese tipo (no cambia la app predeterminada: la elige el usuario)
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
; Registro de la aplicación (nombre en "Abrir con" y tipos que admite)
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
