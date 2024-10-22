#ifdef X64
  #define FilesPath "win-x64"
#else
  #define FilesPath "win-x86"
#endif

#define Title "AM Downloader"
#define Version GetVersionNumbersString("..\AM Downloader\bin\Release\net8.0-windows7.0\publish\" + FilesPath + "\AM Downloader.exe")
#define Publisher "Antik Mozib"
#define Url "https://mozib.io/amdownloader"
#define ExeName "AM Downloader.exe"

#ifdef X64
  #define SetupFileName "AMDownloader-" + Version + "_x64-setup"
#else
  #define SetupFileName "AMDownloader-" + Version + "_x86-setup"
#endif

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{9145540C-3516-4F46-ADC9-E6F25D4ECB11}
AppName={#Title}
AppVersion={#Version}
AppVerName={#Title} {#Version}
AppPublisher={#Publisher}
AppPublisherURL={#Url}
AppSupportURL={#Url}
AppUpdatesURL={#Url}
DefaultDirName={autopf}\{#Title}
DefaultGroupName={#Title}
LicenseFile={#SourcePath}\..\LICENSE
OutputDir=output
OutputBaseFilename={#SetupFileName}
Compression=lzma
SolidCompression=yes
UsePreviousAppDir=True
UninstallDisplayName={#Title}
UninstallDisplayIcon={app}\{#ExeName}
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=commandline dialog
CloseApplications=yes

#ifdef X64
  ArchitecturesAllowed=x64compatible
  ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: 

[Files]
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net8.0-windows7.0\publish\{#FilesPath}\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#Title}"; Filename: "{app}\{#ExeName}"
Name: "{group}\{cm:UninstallProgram,{#Title}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Title}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#StringChange(Title, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
