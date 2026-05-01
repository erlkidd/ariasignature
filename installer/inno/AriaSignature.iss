; AriaSignature installer script (Inno Setup 6)
; Build binaries first, then run this script in Inno Setup Compiler.

#define MyAppName "AriaSignature"
#define MyAppVersion "0.2.4"
#define MyAppPublisher "AriaSignature"
#define MyAppExeName "AriaSignature.UI.exe"
#define MyServiceExeName "AriaSignature.Service.exe"

[Setup]
AppId={{0A0F93B5-6107-4F58-B9CF-6B90D1EA6C95}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\..\artifacts\installer
OutputBaseFilename=AriaSignature-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=no
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\ui\{#MyAppExeName}
SetupIconFile=..\..\icon.ico
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
russian.LaunchProgram=Запустить AriaSignature
russian.OpenProgramGroup=Открыть папку программы
russian.StopServiceOnUninstall=Остановка службы AriaSignature

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"
Name: "autostarttray"; Description: "Запускать AriaSignature при входе в Windows (в трее)"; GroupDescription: "Автозапуск:"

[Files]
Source: "..\..\publish\ui\*"; DestDir: "{app}\ui"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\..\publish\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\smartctl\*"; DestDir: "{app}\service\smartctl"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\webview2\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion

[Icons]
Name: "{group}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{autodesktop}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{commonstartup}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; Parameters: "--tray"; Tasks: autostarttray; IconFilename: "{app}\ui\Assets\icon.ico"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Установка Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated skipifsilent; Check: NeedsWebView2Runtime()

[UninstallRun]

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  ServiceName = 'AriaSignatureService';
  SC_ACCEPTABLE_NOT_FOUND = 1060;
  SC_ACCEPTABLE_NOT_ACTIVE = 1062;
  SC_ACCEPTABLE_ALREADY_RUNNING = 1056;
  SC_MARKED_FOR_DELETE = 1072;
  SC_ALREADY_EXISTS = 1073;
  SC_ACCESS_DENIED = 5;
  WebView2ClientGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  LastScExitCode: Integer;

function ScExePath: string;
begin
  // 64-битный sc.exe: при 32-битном установщике {sys} может указывать на SysWOW64
  Result := ExpandConstant('{sysnative}\sc.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{win}\System32\sc.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{sys}\sc.exe');
end;

function ExecSc(const Params: string; const AcceptableCodeA: Integer; const AcceptableCodeB: Integer): Boolean;
var
  ExitCode: Integer;
begin
  LastScExitCode := -1;
  Result := Exec(ScExePath, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  LastScExitCode := ExitCode;
  if not Result then
  begin
    Log(Format('Failed to execute sc.exe (%s) with params: %s', [ScExePath, Params]));
    Exit;
  end;

  if (ExitCode <> 0) and (ExitCode <> AcceptableCodeA) and (ExitCode <> AcceptableCodeB) then
  begin
    Log(Format('sc.exe failed. Params=%s ExitCode=%d', [Params, ExitCode]));
    Result := False;
    Exit;
  end;

  Result := True;
end;

function WaitServiceAbsent(const TimeoutSeconds: Integer): Boolean;
var
  ExitCode: Integer;
  I: Integer;
begin
  for I := 1 to TimeoutSeconds do
  begin
    if not Exec(ScExePath, 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      ExitCode := -1;
    end;

    if ExitCode = SC_ACCEPTABLE_NOT_FOUND then
    begin
      Result := True;
      Exit;
    end;

    Sleep(1000);
  end;

  Result := False;
end;

function ServiceIsRegistered: Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(ScExePath, 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function IsWebView2InstalledInRoot(const Root: Integer): Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(Root,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2ClientGuid,
    'pv',
    Version) and (Trim(Version) <> '');
end;

function NeedsWebView2Runtime(): Boolean;
begin
  Result := not IsWebView2InstalledInRoot(HKLM64) and
            not IsWebView2InstalledInRoot(HKLM) and
            not IsWebView2InstalledInRoot(HKCU);
end;

procedure StopAndDeleteServiceBestEffort();
begin
  ExecSc(Format('stop %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
  ExecSc(Format('delete %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
end;

procedure KillServiceProcessBestEffort();
var
  ExitCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AriaSignature.Api.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

procedure InstallServiceOrAbort();
var
  BinPath: string;
  CreateParams: string;
  Attempt: Integer;
  Created: Boolean;
begin
  if not IsAdminInstallMode then
  begin
    RaiseException('Установка требует прав администратора: без них служба Windows не может быть зарегистрирована. Запустите установщик от имени администратора.');
  end;

  StopAndDeleteServiceBestEffort();
  if not WaitServiceAbsent(25) then
  begin
    Log('Service still exists after delete wait timeout; will continue with create retries.');
  end;

  BinPath := ExpandConstant('{app}\service\{#MyServiceExeName}');
  if not FileExists(BinPath) then
  begin
    RaiseException('Не найден файл службы: ' + BinPath);
  end;

  { binPath в кавычках (AddQuotes): иначе "Program Files" ломает sc create и служба не регистрируется }
  CreateParams := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature" obj= LocalSystem';

  Created := False;
  for Attempt := 1 to 8 do
  begin
    if ExecSc(CreateParams, 0, -1) then
    begin
      Created := True;
      Break;
    end;

    Log(Format('sc create retry %d failed with code %d', [Attempt, LastScExitCode]));
    if (LastScExitCode <> SC_MARKED_FOR_DELETE) and
       (LastScExitCode <> SC_ALREADY_EXISTS) and
       (LastScExitCode <> SC_ACCESS_DENIED) then
    begin
      Break;
    end;

    Sleep(1500);
  end;

  if not Created then
  begin
    RaiseException(
      'Не удалось зарегистрировать службу AriaSignatureService (sc create). Код sc.exe: ' +
      IntToStr(LastScExitCode) + '. См. лог установщика.');
  end;

  if not ServiceIsRegistered then
  begin
    RaiseException('Служба AriaSignatureService не найдена в системе сразу после регистрации. Проверьте антивирус и политики (запрет изменения служб).');
  end;

  if not ExecSc(Format('failure %s reset= 86400 actions= restart/5000/restart/5000/restart/5000', [ServiceName]), 0, -1) then
  begin
    RaiseException('Не удалось настроить recovery policy службы AriaSignatureService');
  end;

  if not ExecSc(Format('start %s', [ServiceName]), 0, SC_ACCEPTABLE_ALREADY_RUNNING) then
  begin
    RaiseException('Не удалось запустить службу AriaSignatureService');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopAndDeleteServiceBestEffort();
    KillServiceProcessBestEffort();
    if not WaitServiceAbsent(25) then
    begin
      Log('Service still exists before file copy; installer continues and will retry create later.');
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    InstallServiceOrAbort();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAndDeleteServiceBestEffort();
    KillServiceProcessBestEffort();
  end;
end;
