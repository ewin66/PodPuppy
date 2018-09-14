; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "PodPuppy"
#define MyAppVerName "PodPuppy 0.7.0.0"
#define MyAppPublisher "Felix Watts"
#define MyAppURL "http://www.podpuppy.net"
#define MyAppExeName "PodPuppy.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{93077FB9-8DEB-4442-9BB0-31FDF1F5675C}
AppName={#MyAppName}
AppVerName={#MyAppVerName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
LicenseFile=C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\PodPuppy User Licence.txt
OutputDir=C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\Installer
OutputBaseFilename=PodPuppySetup
SetupIconFile=C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\PodPuppy.ico
Compression=lzma
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\bin\Release\PodPuppy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\bin\Release\ID3Sharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\bin\Release\PodPuppy.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\Help\PodPuppy User Guide.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\Help\stylesheet.css"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\Help\Tags.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\Help\images\*"; DestDir: "{app}\images"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\KeyedListLicence.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\PodPuppy.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\felix\Documents\Visual Studio 2008\Projects\PodPuppy\id3sharp\ID3Sharp License.txt"; DestDir: "{app}"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

