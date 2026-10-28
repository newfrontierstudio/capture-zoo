[Setup]
AppId={{0C7E70AD-F371-48F7-9778-2162C98EA2DC}
AppName=CaptureZoo
AppVersion=1.0.13
AppPublisher=newfrontierstudio
AppPublisherURL=https://github.com/newfrontierstudio/capture-zoo
DefaultDirName={localappdata}\Programs\CaptureZoo
DefaultGroupName=CaptureZoo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=CaptureZoo-1.0.13-Setup-x64
SetupIconFile=..\capture.ico
UninstallDisplayIcon={app}\CaptureZoo.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
japanese.DesktopIcon=デスクトップにショートカットを作成
english.LaunchApp=Launch CaptureZoo
japanese.LaunchApp=CaptureZooを起動
english.RuntimeTitle=Preparing runtime
japanese.RuntimeTitle=実行環境の準備
english.RuntimeDownload=Downloading Microsoft .NET Desktop Runtime.
japanese.RuntimeDownload=Microsoft .NET Desktop Runtimeを取得しています。
english.RuntimeNetwork=Could not download the runtime. Check your connection and try again.
japanese.RuntimeNetwork=実行環境を取得できませんでした。接続を確認して再試行してください。
english.RuntimeMissing=The runtime is not installed. Connect to the Internet and run the installer again.
japanese.RuntimeMissing=実行環境が未導入です。インターネットに接続してインストーラーを再実行してください。
english.RuntimeCancelled=Runtime installation was cancelled. Administrator permission is required.
japanese.RuntimeCancelled=実行環境の導入が中止されました。管理者の許可が必要です。
english.RuntimeError=Could not install the runtime. Error code:
japanese.RuntimeError=実行環境を導入できませんでした。エラーコード:
english.RuntimeCheck=Could not verify the runtime. Restart Windows and try again.
japanese.RuntimeCheck=実行環境を確認できません。Windowsを再起動して再試行してください。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "..\artifacts\app-light\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"
Name: "{autodesktop}\CaptureZoo"; Filename: "{app}\CaptureZoo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CaptureZoo.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

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
  RuntimeDownload := CreateDownloadPage(CustomMessage('RuntimeTitle'),
    CustomMessage('RuntimeDownload'), nil);
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
          SuppressibleMsgBox(CustomMessage('RuntimeNetwork') + #13#10 + GetExceptionMessage, mbError, MB_OK, IDOK);
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
    Result := CustomMessage('RuntimeMissing'); Exit;
  end;
  if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
    '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := CustomMessage('RuntimeCancelled'); Exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then begin
    Result := CustomMessage('RuntimeError') + ' ' + IntToStr(ExitCode); Exit;
  end;
  if not HasDesktopRuntime then
    Result := CustomMessage('RuntimeCheck');
  NeedsRestart := ExitCode = 3010;
end;
