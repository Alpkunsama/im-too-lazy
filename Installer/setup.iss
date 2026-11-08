#ifndef MyPayloadDir
#define MyPayloadDir "payload"
#endif
#ifndef MyAppVersion
#define MyAppVersion "0.0031"
#endif
#ifndef MyFileVersion
#define MyFileVersion "0.0.0.31"
#endif
#ifndef WebViewInstaller
#define WebViewInstaller "prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#endif
[Setup]
AppId={{9C610768-F580-4625-AE0B-976316CEB3D8}
AppName=im too lazy
AppVersion={#MyAppVersion}
AppVerName=im too lazy
AppPublisher=im too lazy
DefaultDirName={localappdata}\Programs\im too lazy
DefaultGroupName=im too lazy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=im-too-lazy-{#MyAppVersion}-setup
SetupIconFile=..\App\app.ico
UninstallDisplayIcon={app}\im too lazy.exe
UninstallDisplayName=im too lazy
Uninstallable=yes
CreateUninstallRegKey=yes
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\ImTooLazySubtitleController
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyFileVersion}
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"
[Files]
Source: "{#MyPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
Source: "{#WebViewInstaller}"; Flags: dontcopy
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\im too lazy"; Filename: "{app}\im too lazy.exe"; WorkingDir: "{app}"; AppUserModelID: "ImTooLazy.SubtitleController"
Name: "{group}\im too lazy Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\im too lazy"; Filename: "{app}\im too lazy.exe"; WorkingDir: "{app}"; AppUserModelID: "ImTooLazy.SubtitleController"; Tasks: desktopicon
[Run]
Filename: "{app}\im too lazy.exe"; Description: "im too lazy uygulamasını aç"; Flags: nowait postinstall skipifsilent
[Code]
function ValidRuntimeVersion(RootKey: Integer): Boolean;
var Version: String;
begin
  Result := RegQueryStringValue(RootKey, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;
function HasWebView2: Boolean;
begin
  Result := ValidRuntimeVersion(HKLM32) or ValidRuntimeVersion(HKCU32) or ValidRuntimeVersion(HKCU64);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if not HasWebView2 then begin
    WizardForm.StatusLabel.Caption := 'Microsoft WebView2 kuruluyor...';
    ExtractTemporaryFile('MicrosoftEdgeWebView2RuntimeInstallerX64.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'WebView2 kurucusu başlatılamadı. Kurulumu tekrar deneyin.'
    else if not HasWebView2 then
      Result := 'WebView2 kurulumu tamamlanamadı (kod ' + IntToStr(ExitCode) + '). Bilgisayarı yeniden başlatıp tekrar deneyin.';
  end;
end;
