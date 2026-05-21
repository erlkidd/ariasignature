; AriaSignature installer script (Inno Setup 6)
; Build binaries first, then run this script in Inno Setup Compiler.

#define MyAppName "AriaSignature"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "AriaSignature"
#define MyAppExeName "AriaSignature.UI.exe"
#define MyServiceExeName "AriaSignature.Service.exe"
#define MyMelezhHostExeName "AriaSignature.MelezhHost.exe"

[Setup]
AppId={{0A0F93B5-6107-4F58-B9CF-6B90D1EA6C95}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
UsePreviousAppDir=no
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
SetupIconFile=..\..\assets\branding\icon.ico
CloseApplications=yes
CloseApplicationsFilter=AriaSignature.UI.exe,AriaSignature.Service.exe,AriaSignature.Api.exe,AriaSignature.MelezhHost.exe
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
russian.LaunchProgram=Запустить AriaSignature
russian.OpenProgramGroup=Открыть папку программы
russian.StopServiceOnUninstall=Остановка службы AriaSignature

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"

[Files]
Source: "..\..\publish\ui\*"; DestDir: "{app}\ui"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\..\publish\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs createallsubdirs ignoreversion restartreplace
Source: "..\..\publish\melezh-host\*"; DestDir: "{app}\melezh-host"; Flags: recursesubdirs createallsubdirs ignoreversion restartreplace
Source: "..\melezh\bundle\*"; DestDir: "{app}\melezh"; Flags: recursesubdirs createallsubdirs ignoreversion restartreplace
Source: "..\smartctl\*"; DestDir: "{app}\service\smartctl"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion; Check: NeedsWebView2Runtime

[Icons]
Name: "{group}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; WorkingDir: "{app}\ui"; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{autodesktop}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; WorkingDir: "{app}\ui"; Tasks: desktopicon; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{userstartup}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; WorkingDir: "{app}\ui"; Parameters: "--tray"; IconFilename: "{app}\ui\Assets\icon.ico"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AriaSignature"; ValueData: """{app}\ui\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Установка Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated skipifsilent; Check: NeedsWebView2Runtime()
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AriaSignature API (TCP 5160)"" dir=in action=allow protocol=TCP localport=5160"; StatusMsg: "Разрешение входящих подключений к API (порт 5160)..."; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AriaSignature Melezh (TCP 7788)"" dir=in action=allow protocol=TCP localport=7788"; StatusMsg: "Разрешение входящих подключений к Melezh (порт 7788)..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AriaSignature API (TCP 5160)"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewallRule-AriaApi5160"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AriaSignature Melezh (TCP 7788)"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewallRule-AriaMelezh7788"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  TrayTaskName = 'AriaSignatureTrayLogon';
  ServiceName = 'AriaSignatureService';
  MelezhServiceName = 'AriaSignatureMelezhService';
  PostInstallTimeoutSeconds = 120;
  SC_ACCEPTABLE_NOT_FOUND = 1060;
  SC_ACCEPTABLE_NOT_ACTIVE = 1062;
  SC_ACCEPTABLE_ALREADY_RUNNING = 1056;
  SC_MARKED_FOR_DELETE = 1072;
  SC_ALREADY_EXISTS = 1073;
  SC_ACCESS_DENIED = 5;
  WebView2ClientGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  ScSvcNotRegistered = 0;
  ScSvcStopped = 1;
  ScSvcRunning = 2;
  ScSvcStartPending = 3;
  ScSvcStopPending = 4;
  ScSvcDeletePending = 5;
  ScSvcUnknown = 6;

var
  LastScExitCode: Integer;
  LastProbeExitCode: Integer;
  InstallHealthStatus: string;
  LastBootstrapExitCode: Integer;
  LastBootstrapErrorClass: string;
  LastBootstrapFailureDetail: string;
  PostInstallBudgetSecondsLeft: Integer;
  PostInstallTimedOut: Boolean;

function ScExePath: string;
begin
  // 64-битный sc.exe: при 32-битном установщике {sys} может указывать на SysWOW64
  Result := ExpandConstant('{sysnative}\sc.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{win}\System32\sc.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{sys}\sc.exe');
end;

procedure PumpWizardUi(const StatusText: string);
begin
  { В uninstall-контексте WizardForm недоступен; используем best-effort и не падаем. }
  try
    if Assigned(WizardForm) then
    begin
      if StatusText <> '' then
        WizardForm.StatusLabel.Caption := StatusText;
      WizardForm.Update;
      Exit;
    end;
  except
    { ignore: fallback to uninstall form }
  end;

  try
    if Assigned(UninstallProgressForm) then
    begin
      if StatusText <> '' then
        UninstallProgressForm.StatusLabel.Caption := StatusText;
      UninstallProgressForm.Update;
    end;
  except
    { ignore }
  end;
end;

procedure SleepWithWizardUi(const DelayMs: Integer; const StatusText: string);
var
  Remaining: Integer;
  SliceMs: Integer;
begin
  Remaining := DelayMs;
  while Remaining > 0 do
  begin
    PumpWizardUi(StatusText);
    if Remaining > 200 then
      SliceMs := 200
    else
      SliceMs := Remaining;
    Sleep(SliceMs);
    Remaining := Remaining - SliceMs;
  end;
  PumpWizardUi(StatusText);
  PostInstallBudgetSecondsLeft := PostInstallBudgetSecondsLeft - (DelayMs div 1000);
end;

procedure StartPostInstallBudget();
begin
  PostInstallTimedOut := False;
  PostInstallBudgetSecondsLeft := PostInstallTimeoutSeconds;
end;

function IsPostInstallTimedOut(const StageName: string): Boolean;
begin
  if PostInstallTimedOut then
  begin
    Result := True;
    Exit;
  end;

  Result := PostInstallBudgetSecondsLeft <= 0;
  if Result then
  begin
    PostInstallTimedOut := True;
    InstallHealthStatus := 'install-health:timeout';
    Log('Post-install timeout reached at stage: ' + StageName);
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=' + StageName);
    PumpWizardUi('Установка завершает настройку. Служба догревается в фоне...');
  end;
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

procedure KillServiceProcessBestEffort(); forward;
function ScQueryServiceState(const AServiceName: string): Integer; forward;
function WaitServiceAbsent(const AServiceName: string; const TimeoutSeconds: Integer): Boolean; forward;
procedure FastRemoveServiceBestEffort(const AServiceName: string); forward;

function ScQueryServiceState(const AServiceName: string): Integer;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
  LineUpper: string;
begin
  Result := ScSvcUnknown;
  TempFile := ExpandConstant('{tmp}\aria-sc-state-' + AServiceName + '.txt');
  DeleteFile(TempFile);
  if not Exec(
       ExpandConstant('{sys}\cmd.exe'),
       '/c "' + ScExePath + '" query ' + AServiceName + ' > "' + TempFile + '" 2>&1',
       '',
       SW_HIDE,
       ewWaitUntilTerminated,
       ExitCode) then
    Exit;

  if ExitCode = SC_ACCEPTABLE_NOT_FOUND then
  begin
    Result := ScSvcNotRegistered;
    Exit;
  end;

  if LoadStringsFromFile(TempFile, Lines) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      LineUpper := UpperCase(Lines[I]);
      if (Pos('DELETE_PENDING', LineUpper) > 0) or
         (Pos('MARKED FOR DELETE', LineUpper) > 0) or
         (Pos('MARKED_FOR_DELETE', LineUpper) > 0) then
      begin
        Result := ScSvcDeletePending;
        Exit;
      end;
      if Pos('RUNNING', LineUpper) > 0 then
      begin
        Result := ScSvcRunning;
        Exit;
      end;
      if Pos('START_PENDING', LineUpper) > 0 then
      begin
        Result := ScSvcStartPending;
        Exit;
      end;
      if Pos('STOP_PENDING', LineUpper) > 0 then
      begin
        Result := ScSvcStopPending;
        Exit;
      end;
      if Pos('STOPPED', LineUpper) > 0 then
      begin
        Result := ScSvcStopped;
        Exit;
      end;
    end;
  end;
end;

procedure FastRemoveServiceBestEffort(const AServiceName: string);
var
  State: Integer;
  I: Integer;
begin
  State := ScQueryServiceState(AServiceName);
  Log(Format('marker=fast-remove-service service=%s state=%d', [AServiceName, State]));

  if State = ScSvcNotRegistered then
  begin
    Log('marker=fast-remove-service status=ok reason=not-registered');
    Exit;
  end;

  if (State = ScSvcRunning) or (State = ScSvcStartPending) or (State = ScSvcStopPending) then
  begin
    KillServiceProcessBestEffort();
    ExecSc(Format('stop %s', [AServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
  end;

  ExecSc(Format('delete %s', [AServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);

  if State = ScSvcDeletePending then
  begin
    for I := 1 to 10 do
    begin
      if WaitServiceAbsent(AServiceName, 1) then
      begin
        Log('marker=fast-remove-service status=ok mode=wait-delete-pending');
        Exit;
      end;
      KillServiceProcessBestEffort();
    end;
    Log('marker=fast-remove-service status=degraded reason=delete-pending');
  end
  else
    Log('marker=fast-remove-service status=ok');
end;

function WaitServiceAbsent(const AServiceName: string; const TimeoutSeconds: Integer): Boolean;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
  LineUpper: string;
  LineIndex: Integer;
  HasPendingDeleteMarker: Boolean;
  WaitCaption: string;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\aria-sc-wait-absent.txt');
  WaitCaption := 'Ожидание освобождения службы ' + AServiceName + '...';

  for I := 1 to TimeoutSeconds do
  begin
    DeleteFile(TempFile);
    if not Exec(
         ExpandConstant('{sys}\cmd.exe'),
         '/c "' + ScExePath + '" query ' + AServiceName + ' > "' + TempFile + '" 2>&1',
         '',
         SW_HIDE,
         ewWaitUntilTerminated,
         ExitCode) then
    begin
      ExitCode := -1;
    end;

    if ExitCode = SC_ACCEPTABLE_NOT_FOUND then
    begin
      Result := True;
      Exit;
    end;

    HasPendingDeleteMarker := False;
    if LoadStringsFromFile(TempFile, Lines) then
    begin
      { Ищем pending-delete маркеры по всему выводу sc query, не только в первой строке. }
      for LineIndex := 0 to GetArrayLength(Lines) - 1 do
      begin
        LineUpper := UpperCase(Lines[LineIndex]);
        if (Pos('DELETE_PENDING', LineUpper) > 0) or
           (Pos('MARKED FOR DELETE', LineUpper) > 0) or
           (Pos('MARKED_FOR_DELETE', LineUpper) > 0) or
           (Pos('MARKED FOR DELETION', LineUpper) > 0) then
        begin
          HasPendingDeleteMarker := True;
          Break;
        end;
      end;

      if HasPendingDeleteMarker then
        Log('WaitServiceAbsent: service is pending delete, keep waiting.')
      else
        Log('WaitServiceAbsent: service still present in SCM, keep waiting and retry stop/delete.');
    end;

    if (I mod 5) = 0 then
    begin
      ExecSc(Format('stop %s', [AServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
      ExecSc(Format('delete %s', [AServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
      KillServiceProcessBestEffort();
    end;

    SleepWithWizardUi(1000, WaitCaption);
  end;
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

function IsServiceRunningViaSc(): Boolean;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\aria-sc-running-check.txt');
  DeleteFile(TempFile);
  if not Exec(
       ExpandConstant('{sys}\cmd.exe'),
       '/c "' + ScExePath + '" query ' + ServiceName + ' > "' + TempFile + '" 2>&1',
       '',
       SW_HIDE,
       ewWaitUntilTerminated,
       ExitCode) then
  begin
    Exit;
  end;

  if not LoadStringsFromFile(TempFile, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos('RUNNING', UpperCase(Lines[I])) > 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function ServiceIsRunningOrStartPendingViaSc(): Boolean;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
  UpperLine: string;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\aria-sc-running-or-pending.txt');
  DeleteFile(TempFile);
  if not Exec(
       ExpandConstant('{sys}\cmd.exe'),
       '/c "' + ScExePath + '" query ' + ServiceName + ' > "' + TempFile + '" 2>&1',
       '',
       SW_HIDE,
       ewWaitUntilTerminated,
       ExitCode) then
  begin
    Exit;
  end;

  if not LoadStringsFromFile(TempFile, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    UpperLine := UpperCase(Lines[I]);
    if (Pos('RUNNING', UpperLine) > 0) or (Pos('START_PENDING', UpperLine) > 0) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AssertFileExistsOrAbort(const PathValue: string; const LabelText: string);
begin
  if not FileExists(PathValue) then
    RaiseException('Preflight failed: missing ' + LabelText + ': ' + PathValue);
end;

function RunBootstrapRepair(const ServiceExePath: string): Boolean;
var
  BootstrapExe: string;
  ExitCode: Integer;
  LastResultPath: string;
  LastLines: TArrayOfString;
begin
  Result := False;
  LastBootstrapExitCode := -1;
  LastBootstrapErrorClass := 'none';
  LastBootstrapFailureDetail := '';
  BootstrapExe := ExpandConstant('{app}\ui\bootstrap\AriaSignature.ServiceBootstrap.exe');
  if not FileExists(BootstrapExe) then
  begin
    LastBootstrapErrorClass := 'bootstrap-missing';
    Log('Auto-repair skipped: bootstrap exe missing: ' + BootstrapExe);
    Exit;
  end;

  Log('Auto-repair: launching bootstrap helper...');
  if not Exec(BootstrapExe, AddQuotes(ServiceExePath), '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    LastBootstrapErrorClass := 'bootstrap-exec-failed';
    Log('Auto-repair failed: unable to execute bootstrap helper.');
    Exit;
  end;

  LastBootstrapExitCode := ExitCode;
  Log(Format('Auto-repair bootstrap exit code: %d', [ExitCode]));
  LastResultPath := ExpandConstant('{commonappdata}\AriaSignature\logs\bootstrap-last-result.txt');
  if LoadStringsFromFile(LastResultPath, LastLines) and (GetArrayLength(LastLines) > 0) then
  begin
    LastBootstrapFailureDetail := LastLines[0];
    Log('Auto-repair bootstrap detail: ' + LastBootstrapFailureDetail);
  end;

  if ExitCode = -2147450726 then
  begin
    LastBootstrapErrorClass := 'host-runtime-missing';
    Log('Auto-repair bootstrap classification: host-runtime-missing');
  end
  else if ExitCode <> 0 then
  begin
    LastBootstrapErrorClass := 'bootstrap-runtime-failed';
    Log('Auto-repair bootstrap classification: bootstrap-runtime-failed');
  end;
  Result := ExitCode = 0;
end;

function BuildBootstrapFailureHint(): string;
begin
  if LastBootstrapErrorClass = 'host-runtime-missing' then
  begin
    Result :=
      'Auto-repair helper не смог стартовать из-за host/runtime ошибки (-2147450726). '#13#10 +
      'Это признак неверной упаковки bootstrap или повреждённой установки. '#13#10 +
      'Проверьте setup log и переустановите сборку с актуальным installer.';
    Exit;
  end;

  Result :=
    'Auto-repair helper завершился ошибкой. '#13#10 +
    'Проверьте %ProgramData%\AriaSignature\logs\ и setup log.'#13#10 +
    'Bootstrap detail: ' + LastBootstrapFailureDetail;
end;

function MelezhServiceIsRegistered: Boolean; forward;
procedure StopAndDeleteMelezhServiceBestEffort(); forward;
procedure InstallMelezhServiceOrAbort(); forward;
function ProbeMelezhUiHealth(const Port: Integer): Boolean; forward;
function VerifyInstalledApiVersionOrAbort(): Boolean; forward;
function GetFileProductVersion(const FilePath: string): string; forward;
procedure AssertOnDiskServiceVersionOrAbort(const ApiExePath: string); forward;
procedure ForceStopAndReleaseServiceProcesses(); forward;
function TryScCreateServiceOnly(const CreateParams: string): Boolean; forward;
procedure AssertMelezhBundleOrAbort(); forward;
function BuildTrayLogonDomainUser(): string; forward;
procedure RemoveAppDirectoryBestEffort(); forward;

procedure StopAndDeleteServiceBestEffort();
begin
  FastRemoveServiceBestEffort(MelezhServiceName);
  FastRemoveServiceBestEffort(ServiceName);
end;

procedure KillServiceProcessBestEffort();
var
  ExitCode: Integer;
  OscriptPath: string;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyMelezhHostExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AriaSignature.Api.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  OscriptPath := ExpandConstant('{app}\melezh\lib\oint\bin\oscript.exe');
  if FileExists(OscriptPath) then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM oscript.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function ProbeLocalApiHealth(const Port: Integer): Boolean;
var
  ExitCode: Integer;
  Cmd: string;
begin
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"try { $r = Invoke-WebRequest -UseBasicParsing -Uri ''http://127.0.0.1:' + IntToStr(Port) + '/api/v1/status'' -TimeoutSec 3; if ($r.StatusCode -eq 200) { exit 0 } else { exit 2 } } catch { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  LastProbeExitCode := ExitCode;
  if not Result then
  begin
    Log('Failed to execute local API health probe via powershell.exe');
    Exit;
  end;
  Result := ExitCode = 0;
end;

function ProbeMelezhUiHealth(const Port: Integer): Boolean;
var
  ExitCode: Integer;
  Cmd: string;
begin
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"try { $r = Invoke-WebRequest -UseBasicParsing -Uri ''http://127.0.0.1:' + IntToStr(Port) + '/ui'' -TimeoutSec 5; if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) { exit 0 } else { exit 2 } } catch { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  LastProbeExitCode := ExitCode;
  if not Result then
  begin
    Log('Failed to execute Melezh UI health probe via powershell.exe');
    Exit;
  end;
  Result := ExitCode = 0;
end;

function GetApiReportedVersion(): string;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  Cmd: string;
begin
  Result := '';
  TempFile := ExpandConstant('{tmp}\aria-api-version.txt');
  DeleteFile(TempFile);
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"try { (Invoke-RestMethod -Uri ''http://127.0.0.1:5160/api/v1/status'' -TimeoutSec 5).version | Out-File -FilePath ''' +
    TempFile + ''' -Encoding ascii -NoNewline; exit 0 } catch { exit 1 }"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Exit;
  if ExitCode <> 0 then
    Exit;
  if LoadStringsFromFile(TempFile, Lines) and (GetArrayLength(Lines) > 0) then
    Result := Trim(Lines[0]);
end;

function GetFileProductVersion(const FilePath: string): string;
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  Cmd: string;
  PsPath: string;
begin
  Result := '';
  if not FileExists(FilePath) then
    Exit;

  TempFile := ExpandConstant('{tmp}\aria-file-version.txt');
  DeleteFile(TempFile);
  PsPath := FilePath;
  StringChangeEx(PsPath, '''', '''''', True);
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"try { (Get-Item -LiteralPath ''' + PsPath + ''').VersionInfo.ProductVersion | Out-File -FilePath ''' +
    TempFile + ''' -Encoding ascii -NoNewline; exit 0 } catch { exit 1 }"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Exit;
  if ExitCode <> 0 then
    Exit;
  if LoadStringsFromFile(TempFile, Lines) and (GetArrayLength(Lines) > 0) then
    Result := Trim(Lines[0]);
end;

function VersionMatchesExpected(const ActualVersion: string): Boolean;
begin
  Result := (ActualVersion <> '') and (Pos('{#MyAppVersion}', ActualVersion) > 0);
end;

procedure AssertOnDiskServiceVersionOrAbort(const ApiExePath: string);
var
  ExpectedVersion: string;
  OnDiskVersion: string;
begin
  ExpectedVersion := '{#MyAppVersion}';
  OnDiskVersion := GetFileProductVersion(ApiExePath);
  Log(Format('On-disk version probe: path=%s expected=%s actual=%s', [ApiExePath, ExpectedVersion, OnDiskVersion]));

  if VersionMatchesExpected(OnDiskVersion) then
    Exit;

  InstallHealthStatus := 'install-health:fail-hard-on-disk-version';
  MsgBox(
    'Файлы службы на диске не обновились до версии ' + ExpectedVersion + '.'#13#10 +
    'Ожидалось: ' + ExpectedVersion + #13#10 +
    'На диске (' + ApiExePath + '): ' + OnDiskVersion + #13#10#13#10 +
    'Вероятно, exe был занят запущенным процессом (отложенная замена до перезагрузки).'#13#10 +
    'Закройте все процессы AriaSignature, перезагрузите ПК и запустите установщик снова от администратора.'#13#10 +
    'Логи: %ProgramData%\AriaSignature\logs\.',
    mbError,
    MB_OK);
  RaiseException('On-disk service binary version mismatch; expected ' + ExpectedVersion + '.');
end;

function VerifyInstalledApiVersionOrAbort(): Boolean;
var
  ExpectedVersion: string;
  ApiVersion: string;
  OnDiskVersion: string;
  ApiExePath: string;
begin
  ExpectedVersion := '{#MyAppVersion}';
  ApiExePath := ExpandConstant('{app}\service\AriaSignature.Api.exe');
  ApiVersion := GetApiReportedVersion();
  OnDiskVersion := GetFileProductVersion(ApiExePath);
  Log(Format('API version probe: expected=%s api=%s on-disk=%s', [ExpectedVersion, ApiVersion, OnDiskVersion]));
  if ApiVersion = '' then
  begin
    Result := False;
    Exit;
  end;
  Result := VersionMatchesExpected(ApiVersion);
end;

function TryScCreateServiceOnly(const CreateParams: string): Boolean;
var
  ExitCode: Integer;
begin
  Result := False;
  if not Exec(ScExePath, CreateParams, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Exit;
  LastScExitCode := ExitCode;
  Result := ExitCode = 0;
end;

procedure AssertMelezhBundleOrAbort();
begin
  AssertFileExistsOrAbort(ExpandConstant('{app}\melezh\bin\melezh.bat'), 'melezh.bat');
  AssertFileExistsOrAbort(ExpandConstant('{app}\melezh\lib\oint\bin\oscript.exe'), 'oscript.exe');
  AssertFileExistsOrAbort(ExpandConstant('{app}\melezh-host\{#MyMelezhHostExeName}'), 'Melezh host exe');
  if not DirExists(ExpandConstant('{app}\melezh\share\oint\lib\oint-cli')) then
  begin
    MsgBox(
      'В установке отсутствует каталог OInt (melezh\share\oint\lib\oint-cli).'#13#10 +
      'Пересоберите installer после scripts\prepare-melezh.ps1.',
      mbError,
      MB_OK);
    RaiseException('OInt bundle incomplete (share/oint-cli missing).');
  end;
end;

procedure ForceStopAndReleaseServiceProcesses();
var
  MainState: Integer;
  MelezhState: Integer;
begin
  MainState := ScQueryServiceState(ServiceName);
  MelezhState := ScQueryServiceState(MelezhServiceName);
  if (MainState = ScSvcNotRegistered) and (MelezhState = ScSvcNotRegistered) then
  begin
    Log('ForceStopAndReleaseServiceProcesses: both services not registered; skip.');
    Exit;
  end;
  if (MainState = ScSvcStopped) and (MelezhState = ScSvcStopped) then
  begin
    Log('ForceStopAndReleaseServiceProcesses: both services stopped; skip sc stop.');
    Exit;
  end;
  if (MainState = ScSvcRunning) or (MainState = ScSvcStartPending) or (MainState = ScSvcStopPending) then
    ExecSc(Format('stop %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
  if (MelezhState = ScSvcRunning) or (MelezhState = ScSvcStartPending) or (MelezhState = ScSvcStopPending) then
    ExecSc(Format('stop %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
  KillServiceProcessBestEffort();
end;

procedure LogScCommandCapture(const ArgsTail: string; const Banner: string);
var
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  TempFile := ExpandConstant('{tmp}\aria-sc-installer-cap.txt');
  DeleteFile(TempFile);
  if Exec(
       ExpandConstant('{sys}\cmd.exe'),
       '/c "' + ScExePath + '" ' + ArgsTail + ' > "' + TempFile + '" 2>&1',
       '',
       SW_HIDE,
       ewWaitUntilTerminated,
       ExitCode) then
  begin
    Log(Banner + ' (cmd exit ' + IntToStr(ExitCode) + ')');
    if LoadStringsFromFile(TempFile, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
        Log(Lines[I]);
    end
    else
      Log('(installer sc capture: file unreadable)');
  end
  else
    Log(Banner + ': cmd.exe capture failed to execute');
end;

procedure InstallServiceOrAbort();
var
  BinPath: string;
  CreateParams: string;
  CreateParamsAlt: string;
  CreateParamsNoDisplay: string;
  Attempt: Integer;
  Created: Boolean;
  Started: Boolean;
  Healthy: Boolean;
  PortAttempt: Integer;
begin
  InstallHealthStatus := 'install-health:starting';
  Log('marker=install-health status=' + InstallHealthStatus + ' stage=enter');
  PumpWizardUi('Настройка службы AriaSignature...');

  if not IsAdminInstallMode then
  begin
    RaiseException('Установка требует прав администратора: без них служба Windows не может быть зарегистрирована. Запустите установщик от имени администратора.');
  end;

  AssertFileExistsOrAbort(ExpandConstant('{app}\ui\{#MyAppExeName}'), 'UI exe');
  BinPath := ExpandConstant('{app}\service\{#MyServiceExeName}');
  AssertFileExistsOrAbort(BinPath, 'service exe');
  AssertFileExistsOrAbort(ExpandConstant('{app}\ui\bootstrap\AriaSignature.ServiceBootstrap.exe'), 'bootstrap exe');
  AssertFileExistsOrAbort(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), 'powershell.exe');
  AssertFileExistsOrAbort(ScExePath, 'sc.exe');
  AssertFileExistsOrAbort(ExpandConstant('{app}\service\smartctl\smartctl.exe'), 'smartctl.exe');
  AssertFileExistsOrAbort(ExpandConstant('{app}\service\smartctl\drivedb.h'), 'drivedb.h');
  if NeedsWebView2Runtime() then
    AssertFileExistsOrAbort(ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'), 'WebView2 offline installer');

  PumpWizardUi('Остановка предыдущей службы перед регистрацией...');
  StopAndDeleteServiceBestEffort();
  ForceStopAndReleaseServiceProcesses();
  if ScQueryServiceState(ServiceName) <> ScSvcNotRegistered then
  begin
    InstallHealthStatus := 'install-health:degraded-service-stuck-precreate';
    Log('Warning: AriaSignatureService still present in SCM before create; continuing with create retries.');
    FastRemoveServiceBestEffort(ServiceName);
  end;

  { binPath в кавычках (AddQuotes): иначе "Program Files" ломает sc create и служба не регистрируется }
  CreateParams := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature" obj= LocalSystem';
  CreateParamsAlt := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature Service" obj= LocalSystem';
  CreateParamsNoDisplay := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto obj= LocalSystem';

  Created := False;
  for Attempt := 1 to 20 do
  begin
    if IsPostInstallTimedOut('create-service') then
    begin
      InstallHealthStatus := 'install-health:degraded-timeout-create';
      Log('marker=install-health status=' + InstallHealthStatus + ' stage=create-service');
      Exit;
    end;

    PumpWizardUi(Format('Регистрация службы AriaSignature (%d/20)...', [Attempt]));
    if TryScCreateServiceOnly(CreateParams) then
    begin
      Created := True;
      Break;
    end;

    Log(Format('sc create retry %d failed with code %d', [Attempt, LastScExitCode]));

    if (LastScExitCode = SC_ALREADY_EXISTS) or (LastScExitCode = 1073) then
    begin
      Log('Service already exists in SCM; forcing delete before recreate.');
      ExecSc(Format('stop %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
      ExecSc(Format('delete %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
      KillServiceProcessBestEffort();
      if not WaitServiceAbsent(ServiceName, 15) then
        Log('WaitServiceAbsent after forced delete timed out; will retry create.');
      SleepWithWizardUi(2000, 'Ожидание удаления предыдущей записи службы...');
    end
    else if LastScExitCode = 1078 then
    begin
      Log('sc create returned 1078 (display name conflict), retry with alternate DisplayName.');
      if TryScCreateServiceOnly(CreateParamsAlt) then
      begin
        Created := True;
        Break;
      end;

      if LastScExitCode = 1078 then
      begin
        Log('sc create still returned 1078, retry without DisplayName.');
        if TryScCreateServiceOnly(CreateParamsNoDisplay) then
        begin
          Created := True;
          Break;
        end;
      end;

      if (LastScExitCode = SC_ALREADY_EXISTS) or (LastScExitCode = 1073) then
      begin
        ExecSc(Format('delete %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
        WaitServiceAbsent(ServiceName, 10);
      end;
    end
    else if LastScExitCode = SC_MARKED_FOR_DELETE then
    begin
      Log('Service is marked for delete; waiting for SCM to finalize deletion before next create.');
      if not WaitServiceAbsent(ServiceName, 15) then
        Log('WaitServiceAbsent after SC_MARKED_FOR_DELETE timed out; will still retry create.');
      SleepWithWizardUi(2500, 'Ожидание обновления состояния службы в Windows...');
    end
    else if (LastScExitCode <> SC_MARKED_FOR_DELETE) and (LastScExitCode <> SC_ALREADY_EXISTS) and (LastScExitCode <> 1073) then
    begin
      Break;
    end
    else
    begin
      SleepWithWizardUi(1500, 'Повторная попытка регистрации службы...');
    end;
  end;

  if not Created then
  begin
    InstallHealthStatus := 'install-health:degraded-service-create-failed';
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=create-service');
    Log('Warning: service create did not succeed; installer continues in degraded mode.');
    Log('marker=install-user-notice status=degraded-service-create skipped=msgbox');
    Exit;
  end;

  if not ServiceIsRegistered then
  begin
    InstallHealthStatus := 'install-health:degraded-service-not-registered';
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=service-registered-check');
    Log('Warning: service registration did not appear in SCM immediately; installer continues in degraded mode.');
    Exit;
  end;

  if not ExecSc(Format('failure %s reset= 86400 actions= restart/5000/restart/5000/restart/5000', [ServiceName]), 0, -1) then
  begin
    Log('Warning: failed to configure recovery policy for AriaSignatureService; installer continues.');
  end;

  Started := False;
  for Attempt := 1 to 12 do
  begin
    if IsPostInstallTimedOut('start-service') then
    begin
      InstallHealthStatus := 'install-health:degraded-timeout-start';
      Log('marker=install-health status=' + InstallHealthStatus + ' stage=start-service');
      Exit;
    end;

    PumpWizardUi(Format('Запуск службы AriaSignature (%d/12)...', [Attempt]));
    if ExecSc(Format('start %s', [ServiceName]), 0, SC_ACCEPTABLE_ALREADY_RUNNING) then
    begin
      Started := True;
      Break;
    end;

    Log(Format('sc start retry %d failed with code %d', [Attempt, LastScExitCode]));
    SleepWithWizardUi(1500, 'Повторный запуск службы...');
  end;

  if not Started then
  begin
    Log('Warning: AriaSignatureService was installed but did not start during setup.');
    Log(Format('Installer binPath (same layout as UI ..\\service\\): %s', [BinPath]));
    Log(Format('Last sc start exit code after retries: %d', [LastScExitCode]));
    LogScCommandCapture('query ' + ServiceName, 'Full sc query output after failed start');
    LogScCommandCapture('qc ' + ServiceName, 'Full sc qc output after failed start');
    InstallHealthStatus := 'install-health:fail-with-repair';
    if RunBootstrapRepair(BinPath) then
    begin
      Started := IsServiceRunningViaSc();
      if Started then
        Log('Auto-repair succeeded: service is RUNNING after bootstrap.');
    end;

    if not Started then
    begin
      InstallHealthStatus := 'install-health:degraded-service-not-running';
      Log('marker=install-health status=' + InstallHealthStatus + ' stage=start-service');
      Log('Warning: service did not start after auto-repair; installer will continue with degraded startup path.');
      Log('marker=install-user-notice status=degraded-service-not-running skipped=msgbox');
      Exit;
    end;
  end;

  Healthy := ProbeLocalApiHealth(5160);
  if not Healthy then
  begin
    for PortAttempt := 1 to 3 do
    begin
      if IsPostInstallTimedOut('api-health') then
      begin
        InstallHealthStatus := 'install-health:degraded-timeout-api';
        Log('marker=install-health status=' + InstallHealthStatus + ' stage=api-health');
        Exit;
      end;

      PumpWizardUi(Format('Проверка локального API (%d/3)...', [PortAttempt]));
      if ProbeLocalApiHealth(5160) then
      begin
        Healthy := True;
        Break;
      end;
      Log(Format('Health probe attempt %d failed with code %d', [PortAttempt, LastProbeExitCode]));
      SleepWithWizardUi(1500, 'Ожидание готовности локального API...');
    end;
  end;

  if not Healthy then
  begin
    InstallHealthStatus := 'install-health:fail-with-repair';
    if not RunBootstrapRepair(BinPath) then
    begin
      InstallHealthStatus := 'install-health:degraded-api-not-ready';
      Log('marker=install-health status=' + InstallHealthStatus + ' stage=api-health');
      Log('Warning: API health check failed and bootstrap repair also failed; installer will continue with degraded startup path.');
      Log('marker=install-user-notice status=degraded-api-not-ready skipped=msgbox');
      Exit;
    end;

    Healthy := ProbeLocalApiHealth(5160);
    if not Healthy then
    begin
      InstallHealthStatus := 'install-health:degraded-api-warmup';
      Log('marker=install-health status=' + InstallHealthStatus + ' stage=api-health');
      Log('Warning: API still not ready after auto-repair; installer continues; UI will retry automatically.');
      Log('marker=install-user-notice status=degraded-api-warmup skipped=msgbox');
      Exit;
    end;
  end;

  if not VerifyInstalledApiVersionOrAbort() then
  begin
    InstallHealthStatus := 'install-health:fail-hard-version';
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=api-version');
    MsgBox(
      'Служба запущена, но версия API не совпадает с установщиком.'#13#10 +
      'Ожидалось: {#MyAppVersion}'#13#10 +
      'API /status: ' + GetApiReportedVersion() + #13#10 +
      'Файл на диске: ' + GetFileProductVersion(ExpandConstant('{app}\service\AriaSignature.Api.exe')) + #13#10#13#10 +
      'Закройте все процессы AriaSignature, перезагрузите ПК и переустановите от администратора.'#13#10 +
      'Логи: %ProgramData%\AriaSignature\logs\.',
      mbError,
      MB_OK);
    RaiseException('API version mismatch after install; expected {#MyAppVersion}.');
  end;

  InstallHealthStatus := 'install-health:ok';
  Log('marker=install-health status=' + InstallHealthStatus + ' stage=done');
end;

procedure RunPreInstallCleanup();
begin
  PumpWizardUi('Подготовка к установке...');
  StopAndDeleteServiceBestEffort();
  ForceStopAndReleaseServiceProcesses();
  if ScQueryServiceState(ServiceName) <> ScSvcNotRegistered then
    Log('Warning: AriaSignatureService still in SCM before file copy; post-install will force recreate.');
  if ScQueryServiceState(MelezhServiceName) <> ScSvcNotRegistered then
    Log('Warning: AriaSignatureMelezhService still in SCM before file copy; post-install will force recreate.');
end;

function SchTasksExePath: string;
begin
  Result := ExpandConstant('{sysnative}\schtasks.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{win}\System32\schtasks.exe');
end;

procedure DeleteTrayLogonTaskBestEffort();
var
  ExitCode: Integer;
  SchTasks: string;
begin
  SchTasks := SchTasksExePath;
  if not FileExists(SchTasks) then
    Exit;
  Exec(SchTasks, Format('/Delete /TN %s /F', [TrayTaskName]), '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function BuildTrayLogonDomainUser(): string;
var
  DomainName: string;
  UserName: string;
begin
  DomainName := Trim(ExpandConstant('{%USERDOMAIN%}'));
  UserName := Trim(ExpandConstant('{%USERNAME%}'));
  if UserName = '' then
    UserName := Trim(ExpandConstant('{username}'));
  if DomainName <> '' then
    Result := DomainName + '\' + UserName
  else
    Result := '.\' + UserName;
end;

procedure RegisterTrayLogonTaskBestEffort();
var
  SchTasks: string;
  UiExe: string;
  TaskRun: string;
  DomainUser: string;
  TaskParams: string;
  ExitCode: Integer;
begin
  try
    SchTasks := SchTasksExePath;
    UiExe := ExpandConstant('{app}\ui\{#MyAppExeName}');
    if (not FileExists(SchTasks)) or (not FileExists(UiExe)) then
    begin
      Log('marker=tray-logon-task status=skipped reason=missing-schtasks-or-ui');
      Exit;
    end;

    DeleteTrayLogonTaskBestEffort();
    TaskRun := AddQuotes(UiExe) + ' --tray';
    DomainUser := BuildTrayLogonDomainUser();
    TaskParams := Format('/Create /TN %s /TR %s /SC ONLOGON /RL LIMITED /DELAY 0000:45 /F /RU %s /IT', [TrayTaskName, TaskRun, DomainUser]);
    if Exec(SchTasks, TaskParams, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      if ExitCode <> 0 then
        Log('marker=tray-logon-task status=failed exit_code=' + IntToStr(ExitCode) + ' user=' + DomainUser)
      else
        Log('marker=tray-logon-task status=ok user=' + DomainUser);
    end
    else
      Log('marker=tray-logon-task status=failed reason=schtasks-exec-failed user=' + DomainUser);
  except
    Log('marker=tray-logon-task status=failed reason=exception');
  end;
end;

function MelezhServiceIsRegistered: Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(ScExePath, 'query ' + MelezhServiceName, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

procedure StopAndDeleteMelezhServiceBestEffort();
var
  ExitCode: Integer;
begin
  if MelezhServiceIsRegistered then
  begin
    ExecSc(Format('stop %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
    ExecSc(Format('delete %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyMelezhHostExeName}', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if FileExists(ExpandConstant('{app}\melezh\lib\oint\bin\oscript.exe')) then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM oscript.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

procedure InstallMelezhServiceOrAbort();
var
  BinPath: string;
  CreateParams: string;
  Attempt: Integer;
  Created: Boolean;
  Started: Boolean;
  Healthy: Boolean;
  PortAttempt: Integer;
  MelezhPort: Integer;
begin
  PumpWizardUi('Настройка службы Melezh / OpenIntegrations...');
  BinPath := ExpandConstant('{app}\melezh-host\{#MyMelezhHostExeName}');
  AssertFileExistsOrAbort(BinPath, 'Melezh host exe');
  AssertFileExistsOrAbort(ExpandConstant('{app}\melezh\bin\melezh.bat'), 'melezh.bat');
  AssertFileExistsOrAbort(ExpandConstant('{app}\melezh\lib\oint\bin\oscript.exe'), 'oscript.exe');

  StopAndDeleteMelezhServiceBestEffort();
  if not WaitServiceAbsent(MelezhServiceName, 12) then
    Log('Melezh service still pending in SCM before create; continuing with retries.');

  CreateParams := 'create ' + MelezhServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature Melezh" obj= LocalSystem';
  Created := False;
  for Attempt := 1 to 12 do
  begin
    if IsPostInstallTimedOut('create-melezh-service') then
      RaiseException('Таймаут регистрации службы Melezh.');

    PumpWizardUi(Format('Регистрация службы Melezh (%d/12)...', [Attempt]));
    if TryScCreateServiceOnly(CreateParams) then
    begin
      Created := True;
      Break;
    end;

    if (LastScExitCode = SC_ALREADY_EXISTS) or (LastScExitCode = 1073) then
    begin
      ExecSc(Format('stop %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
      ExecSc(Format('delete %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
      KillServiceProcessBestEffort();
      WaitServiceAbsent(MelezhServiceName, 10);
      SleepWithWizardUi(1500, 'Ожидание удаления предыдущей службы Melezh...');
    end
    else if LastScExitCode = SC_MARKED_FOR_DELETE then
    begin
      if not WaitServiceAbsent(MelezhServiceName, 10) then
        Log('WaitServiceAbsent(Melezh) after SC_MARKED_FOR_DELETE timed out.');
      SleepWithWizardUi(1500, 'Ожидание удаления предыдущей службы Melezh...');
    end
    else
      SleepWithWizardUi(1500, 'Повторная попытка регистрации службы Melezh...');
  end;

  if not Created then
  begin
    InstallHealthStatus := 'install-health:fail-hard-melezh';
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=create-melezh-service');
    MsgBox(
      'Служба AriaSignatureMelezhService не зарегистрирована (sc create код ' + IntToStr(LastScExitCode) + ').'#13#10 +
      'Проверьте, что установщик собран с bundle OInt (prepare-melezh.ps1) и запущен от администратора.',
      mbError,
      MB_OK);
    RaiseException('Melezh service create failed.');
  end;

  if not MelezhServiceIsRegistered then
  begin
    InstallHealthStatus := 'install-health:fail-hard-melezh';
    MsgBox('Служба Melezh не появилась в SCM после sc create.', mbError, MB_OK);
    RaiseException('Melezh service not registered after create.');
  end;

  ExecSc(Format('description %s %s', [MelezhServiceName, 'OpenIntegrations Melezh HTTP gateway for AriaSignature']), 0, 0);
  ExecSc(Format('failure %s reset= 86400 actions= restart/60000/restart/60000/restart/60000', [MelezhServiceName]), 0, 0);

  Started := False;
  for Attempt := 1 to 10 do
  begin
    if IsPostInstallTimedOut('start-melezh-service') then
      RaiseException('Таймаут запуска службы Melezh.');

    PumpWizardUi(Format('Запуск службы Melezh (%d/10)...', [Attempt]));
    if ExecSc(Format('start %s', [MelezhServiceName]), 0, SC_ACCEPTABLE_ALREADY_RUNNING) then
    begin
      Started := True;
      Break;
    end;
    SleepWithWizardUi(1500, 'Повторный запуск службы Melezh...');
  end;

  if not Started then
  begin
    InstallHealthStatus := 'install-health:fail-hard-melezh';
    LogScCommandCapture('query ' + MelezhServiceName, 'Melezh sc query after failed start');
    MsgBox(
      'Служба AriaSignatureMelezhService не запустилась (sc start код ' + IntToStr(LastScExitCode) + ').'#13#10 +
      'Проверьте %ProgramData%\AriaSignature\logs\ и наличие {app}\melezh\bin\melezh.bat.',
      mbError,
      MB_OK);
    RaiseException('Melezh service start failed.');
  end;

  MelezhPort := 7788;
  Healthy := False;
  for PortAttempt := 1 to 10 do
  begin
    if IsPostInstallTimedOut('melezh-ui-health') then
      RaiseException('Таймаут проверки Web UI Melezh.');

    PumpWizardUi(Format('Проверка Web UI Melezh (%d/10)...', [PortAttempt]));
    if ProbeMelezhUiHealth(MelezhPort) then
    begin
      Healthy := True;
      Break;
    end;
    SleepWithWizardUi(2000, 'Ожидание готовности Melezh Web UI...');
  end;

  if not Healthy then
  begin
    if InstallHealthStatus = 'install-health:ok' then
      InstallHealthStatus := 'install-health:degraded-melezh-ui';
    Log('marker=install-health status=' + InstallHealthStatus + ' stage=melezh-ui-health');
    Log('Melezh Web UI health probe failed; service may still be warming up after wizard closes.');
  end
  else
    Log('marker=install-health stage=melezh-ready status=ok');
end;

procedure RemoveLegacyCommonStartupShortcutBestEffort();
begin
  if DeleteFile(ExpandConstant('{commonstartup}\AriaSignature.lnk')) then
    Log('Removed legacy common Startup shortcut AriaSignature.lnk');
end;

procedure RemoveAppDirectoryBestEffort();
var
  AppDir: string;
  Attempt: Integer;
begin
  AppDir := ExpandConstant('{app}');
  if AppDir = '' then
    Exit;

  if not DirExists(AppDir) then
  begin
    Log('marker=uninstall-app-directory status=ok reason=already-absent path=' + AppDir);
    Exit;
  end;

  ForceStopAndReleaseServiceProcesses();
  KillServiceProcessBestEffort();

  for Attempt := 1 to 3 do
  begin
    if DelTree(AppDir, True, True, True) then
    begin
      Log('marker=uninstall-app-directory status=ok path=' + AppDir + ' attempt=' + IntToStr(Attempt));
      Exit;
    end;
    Log('marker=uninstall-app-directory status=retry path=' + AppDir + ' attempt=' + IntToStr(Attempt));
    Sleep(1000);
    KillServiceProcessBestEffort();
  end;

  if DirExists(AppDir) then
  begin
    Log('marker=uninstall-app-directory status=degraded path=' + AppDir);
    MsgBox(
      'Папка программы не удалена полностью:'#13#10 + AppDir + #13#10#13#10 +
      'Закройте все процессы AriaSignature и oscript.exe, перезагрузите ПК при необходимости.'#13#10 +
      'Затем удалите папку вручную или запустите scripts\repair-upgrade.ps1 от администратора.',
      mbInformation,
      MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RunPreInstallCleanup();
  end;

  if CurStep = ssPostInstall then
  begin
    StartPostInstallBudget();
    AssertOnDiskServiceVersionOrAbort(ExpandConstant('{app}\service\AriaSignature.Api.exe'));
    AssertMelezhBundleOrAbort();
    InstallServiceOrAbort();
    if Pos('install-health:fail-hard', InstallHealthStatus) > 0 then
    begin
      MsgBox(
        'Служба AriaSignature не прошла обязательную проверку после установки.'#13#10 +
        'Статус: ' + InstallHealthStatus + #13#10 +
        'Установка прервана. Закройте приложение и повторите установку от администратора.',
        mbError,
        MB_OK);
      RaiseException('AriaSignature service install verification failed: ' + InstallHealthStatus);
    end;

    InstallMelezhServiceOrAbort();
    RegisterTrayLogonTaskBestEffort();
    RemoveLegacyCommonStartupShortcutBestEffort();
    if Pos('install-health:degraded', InstallHealthStatus) = 1 then
      Log('marker=install-user-notice status=' + InstallHealthStatus + ' skipped=msgbox')
    else if InstallHealthStatus = 'install-health:timeout' then
      Log('marker=install-user-notice status=' + InstallHealthStatus + ' skipped=msgbox');
  end;
end;

procedure InitializeWizard();
begin
  WizardForm.BorderIcons := WizardForm.BorderIcons + [biMinimize];
  PumpWizardUi('Подготовка установщика AriaSignature...');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteTrayLogonTaskBestEffort();
    KillServiceProcessBestEffort();
    FastRemoveServiceBestEffort(MelezhServiceName);
    FastRemoveServiceBestEffort(ServiceName);
    if (ScQueryServiceState(MelezhServiceName) = ScSvcNotRegistered) and
       (ScQueryServiceState(ServiceName) = ScSvcNotRegistered) then
      Log('marker=uninstall-service-removal status=ok')
    else
      Log('marker=uninstall-service-removal status=degraded reason=service-still-present-or-pending');

    if DirExists(ExpandConstant('{commonappdata}\AriaSignature\melezh')) then
    begin
      if DelTree(ExpandConstant('{commonappdata}\AriaSignature\melezh'), True, True, True) then
        Log('Removed Melezh project data: %ProgramData%\AriaSignature\melezh')
      else
        Log('Warning: failed to remove Melezh project data directory');
    end;
  end;

  if CurUninstallStep = usPostUninstall then
    RemoveAppDirectoryBestEffort();
end;
