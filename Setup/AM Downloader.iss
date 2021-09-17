#define MyAppName "AM Downloader"
#define MyAppVersion GetVersionNumbersString("..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.exe")
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
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
LicenseFile=gpl-3.0.txt
OutputDir=output
OutputBaseFilename={#MyAppOutputName}-{#MyAppVersion}-setup
Compression=lzma
SolidCompression=yes
UsePreviousAppDir=True
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppName}.exe
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: 

[Files]
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.runtimeconfig.dev.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\AM Downloader.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\AM Downloader\bin\x64\Release\netcoreapp3.1\win-x64\Microsoft.Xaml.Behaviors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\Docs\AM Downloader Help.chm"; DestDir: "{app}"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent