#define MyAppName "lyric-clone"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "lyric-clone"
#define MyAppURL "https://github.com/ymc3151525/lyric-clone"
#define MyAppExeName "LyricClone.exe"

[Setup]
AppId={{8F3C2A1B-9E04-4D7A-B6C1-2E4F7A91D0C8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\lyric-clone
DefaultGroupName=lyric-clone
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=lyric-clone-0.1.0-win-x64-setup
SetupIconFile=..\src\LyricClone.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=LyricClone.exe
MinVersion=10.0
InfoBeforeFile=..\packaging\install-notes.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\lyric-clone"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall lyric-clone"; Filename: "{uninstallexe}"
Name: "{autodesktop}\lyric-clone"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch lyric-clone"; Flags: nowait postinstall skipifsilent
