; SCUM Lab Launcher — установщик (Inno Setup 6).
; Сборка: iscc /DAppVersion=0.2.1 installer\SCUMLabSetup.iss  (build.ps1 -Installer делает это сам)
; Ставится в профиль пользователя без прав администратора, чтобы самообновление лаунчера могло заменять EXE.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "SCUM Lab Launcher"
#define AppExe "SCUMLabLauncher.exe"
#define WebView2Guid "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{6B0E3F2A-5C1D-4E8B-9A57-3C2D1F0B8E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=SCUM Lab
AppPublisherURL=https://github.com/nikedp/scum-launcher
AppSupportURL=https://github.com/nikedp/scum-launcher/issues
AppUpdatesURL=https://github.com/nikedp/scum-launcher/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\SCUM Lab Launcher
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=SCUMLabSetup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.DesktopIcon=Создать ярлык на рабочем столе
en.DesktopIcon=Create a desktop shortcut
ru.InstallingWebView2=Устанавливаем Microsoft Edge WebView2 Runtime…
en.InstallingWebView2=Installing Microsoft Edge WebView2 Runtime…
ru.LaunchApp=Запустить SCUM Lab Launcher
en.LaunchApp=Launch SCUM Lab Launcher
ru.WebView2Failed=Не удалось установить Microsoft Edge WebView2 Runtime (код %1). Лаунчер откроется в упрощённом окне. WebView2 можно поставить вручную с сайта Microsoft.
en.WebView2Failed=Microsoft Edge WebView2 Runtime could not be installed (code %1). The launcher will open in its basic window. You can install WebView2 manually from Microsoft.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
function HasRuntime(Root: Integer; Key: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function NeedsWebView2: Boolean;
begin
  Result := not (
    HasRuntime(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}') or
    HasRuntime(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}') or
    HasRuntime(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssPostInstall) and NeedsWebView2 then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('InstallingWebView2');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Code) or ((Code <> 0) and NeedsWebView2) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('WebView2Failed'), [IntToStr(Code)]), mbInformation, MB_OK, IDOK);
  end;
end;
