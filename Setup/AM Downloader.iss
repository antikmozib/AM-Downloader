#define MyAppName "AM Downloader"
#define MyAppVersion GetVersionNumbersString("..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\AM Downloader.exe")
#define MyAppPublisher "Antik Mozib"
#define MyAppURL "https://mozib.io/amdownloader"
#define MyAppExeName "AM Downloader.exe"
#define MyAppOutputName "AMDownloader"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{9145540C-3516-4F46-ADC9-E6F25D4ECB11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
LicenseFile=gpl-3.0.rtf
OutputDir=output
OutputBaseFilename={#MyAppOutputName}-{#MyAppVersion}-setup
Compression=lzma
SolidCompression=yes
UsePreviousAppDir=True
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppName}.exe
;ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: 

[Files]
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\AM Downloader.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\AM Downloader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\AM Downloader.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\Microsoft.Xaml.Behaviors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\Release\net6.0-windows\publish\Setup\Polly.dll"; DestDir: "{app}"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent