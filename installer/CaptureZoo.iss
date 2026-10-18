[Setup]
AppId={{0C7E70AD-F371-48F7-9778-2162C98EA2DC}
AppName=CaptureZoo
AppVersion=1.0.1
AppPublisher=newfrontierstudio
AppPublisherURL=https://github.com/newfrontierstudio/capture-zoo
DefaultDirName={localappdata}\Programs\CaptureZoo
DefaultGroupName=CaptureZoo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=CaptureZoo-1.0.1-Setup-x64
SetupIconFile=..\capture.ico
UninstallDisplayIcon={app}\CaptureZoo.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成"; Flags: unchecked

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"
Name: "{autodesktop}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CaptureZoo.exe"; Description: "CaptureZooを起動"; Flags: nowait postinstall skipifsilent

