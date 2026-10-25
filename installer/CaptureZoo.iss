[Setup]
AppId={{0C7E70AD-F371-48F7-9778-2162C98EA2DC}
AppName=CaptureZoo
AppVersion=1.0.4
AppPublisher=newfrontierstudio
AppPublisherURL=https://github.com/newfrontierstudio/capture-zoo
DefaultDirName={localappdata}\Programs\CaptureZoo
DefaultGroupName=CaptureZoo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=CaptureZoo-1.0.4-Setup-x64
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
Source: "..\artifacts\app-light\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"
Name: "{autodesktop}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CaptureZoo.exe"; Description: "CaptureZooを起動"; Flags: nowait postinstall skipifsilent

[InstallDelete]
#include "runtime-cleanup.iss"

[Code]
var
  RuntimeDownload: TDownloadWizardPage;

function CompatibleRuntime(Version: String): Boolean;
begin
  Result := (Copy(Version, 1, 4) = '8.0.') and
    (StrToIntDef(Copy(Version, 5, Length(Version)), -1) >= 32);
end;

function RuntimeInRegistry(View: Integer): Boolean;
var Versions: TArrayOfString; I: Integer; Root: String;
begin
  Result := False;
  Root := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if RegGetValueNames(View, Root, Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if CompatibleRuntime(Versions[I]) then begin Result := True; Exit; end;
end;

function HasDesktopRuntime: Boolean;
begin
  Result := RuntimeInRegistry(HKLM32) or RuntimeInRegistry(HKLM64);
end;

procedure InitializeWizard;
begin
  RuntimeDownload := CreateDownloadPage('実行環境の準備',
    'Microsoft .NET Desktop Runtimeを取得しています。', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not HasDesktopRuntime then begin
    RuntimeDownload.Clear;
    RuntimeDownload.Add('https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.32/windowsdesktop-runtime-8.0.32-win-x64.exe',
      'windowsdesktop-runtime.exe', '0f45815f9f032095910af092405c9c6d3844b7fe57252f854d65ed9379b45598');
    RuntimeDownload.Show;
    try
      try RuntimeDownload.Download;
      except
        if not RuntimeDownload.AbortedByUser then
          SuppressibleMsgBox('実行環境を取得できませんでした。接続を確認して再試行してください。' + #13#10 + GetExceptionMessage, mbError, MB_OK, IDOK);
        Result := False;
      end;
    finally RuntimeDownload.Hide; end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if HasDesktopRuntime then Exit;
  if not FileExists(ExpandConstant('{tmp}\windowsdesktop-runtime.exe')) then begin
    Result := '実行環境が未導入です。インターネットに接続してインストーラーを再実行してください。'; Exit;
  end;
  if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
    '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := '実行環境の導入が中止されました。管理者の許可が必要です。'; Exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then begin
    Result := '実行環境を導入できませんでした（コード ' + IntToStr(ExitCode) + '）。'; Exit;
  end;
  if not HasDesktopRuntime then
    Result := '実行環境を確認できないため、CaptureZooの更新を中止しました。Windowsを再起動して再試行してください。';
  NeedsRestart := ExitCode = 3010;
end;
