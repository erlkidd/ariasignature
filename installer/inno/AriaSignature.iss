; AriaSignature installer script (Inno Setup 6)
; Build binaries first, then run this script in Inno Setup Compiler.

#define MyAppName "AriaSignature"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "AriaSignature"
#define MyAppExeName "AriaSignature.UI.exe"
#define MyServiceExeName "AriaSignature.Service.exe"

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
CloseApplicationsFilter=AriaSignature.UI.exe,AriaSignature.Service.exe,AriaSignature.Api.exe
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
Source: "..\..\publish\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\smartctl\*"; DestDir: "{app}\service\smartctl"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion

[Icons]
Name: "{group}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; WorkingDir: "{app}\ui"; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{autodesktop}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; WorkingDir: "{app}\ui"; Tasks: desktopicon; IconFilename: "{app}\ui\Assets\icon.ico"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Установка Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated skipifsilent; Check: NeedsWebView2Runtime()
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AriaSignature API (TCP 5160)"" dir=in action=allow protocol=TCP localport=5160"; StatusMsg: "Разрешение входящих подключений к API (порт 5160)..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AriaSignature API (TCP 5160)"""; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  TrayTaskName = 'AriaSignatureTrayLogon';
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
  LastProbeExitCode: Integer;
  InstallHealthStatus: string;
  LastBootstrapExitCode: Integer;
  LastBootstrapErrorClass: string;
  LastBootstrapFailureDetail: string;

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
  TempFile: string;
  ExitCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
  LineUpper: string;
  LineIndex: Integer;
  HasPendingDeleteMarker: Boolean;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\aria-sc-wait-absent.txt');

  for I := 1 to TimeoutSeconds do
  begin
    DeleteFile(TempFile);
    if not Exec(
         ExpandConstant('{sys}\cmd.exe'),
         '/c "' + ScExePath + '" query ' + ServiceName + ' > "' + TempFile + '" 2>&1',
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
      begin
        { Служба ещё помечена на удаление: ждём дальше. }
      end
      else
      begin
        { Если нет pending-delete, дальше ждать бессмысленно: либо служба всё ещё существует, либо другая ошибка. }
        Break;
      end;
    end;

    Sleep(1000);
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
  AssertFileExistsOrAbort(ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'), 'WebView2 offline installer');

  { binPath в кавычках (AddQuotes): иначе "Program Files" ломает sc create и служба не регистрируется }
  CreateParams := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature" obj= LocalSystem';
  CreateParamsAlt := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto DisplayName= "AriaSignature Service" obj= LocalSystem';
  CreateParamsNoDisplay := 'create ' + ServiceName + ' binPath= ' + AddQuotes(BinPath) + ' start= auto obj= LocalSystem';

  Created := False;
  for Attempt := 1 to 20 do
  begin
    if ExecSc(CreateParams, 0, SC_ALREADY_EXISTS) then
    begin
      Created := True;
      Break;
    end;

    Log(Format('sc create retry %d failed with code %d', [Attempt, LastScExitCode]));
    if (LastScExitCode <> SC_MARKED_FOR_DELETE) and
       (LastScExitCode <> SC_ALREADY_EXISTS) and
       (LastScExitCode <> 1078) then
    begin
      Break;
    end;

    if LastScExitCode = 1078 then
    begin
      Log('sc create returned 1078 (display name conflict), retry with alternate DisplayName.');
      if ExecSc(CreateParamsAlt, 0, SC_ALREADY_EXISTS) then
      begin
        Created := True;
        Break;
      end;

      if LastScExitCode = 1078 then
      begin
        Log('sc create still returned 1078, retry without DisplayName.');
        if ExecSc(CreateParamsNoDisplay, 0, SC_ALREADY_EXISTS) then
        begin
          Created := True;
          Break;
        end;
      end;
    end;

    if LastScExitCode = SC_MARKED_FOR_DELETE then
    begin
      Log('Service is marked for delete; waiting for SCM to finalize deletion before next create.');
      if not WaitServiceAbsent(15) then
        Log('WaitServiceAbsent after SC_MARKED_FOR_DELETE timed out; will still retry create.');
      Sleep(2500);
    end
    else
    begin
      Sleep(1500);
    end;
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
    Log('Warning: failed to configure recovery policy for AriaSignatureService; installer continues.');
  end;

  Started := False;
  for Attempt := 1 to 12 do
  begin
    if ExecSc(Format('start %s', [ServiceName]), 0, SC_ACCEPTABLE_ALREADY_RUNNING) then
    begin
      Started := True;
      Break;
    end;

    Log(Format('sc start retry %d failed with code %d', [Attempt, LastScExitCode]));
    Sleep(1500);
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
      Log('Warning: service did not start after auto-repair; installer will continue with degraded startup path.');
      SuppressibleMsgBox(
        'Служба AriaSignature не была запущена автоматически после установки.'#13#10 +
        BuildBootstrapFailureHint() + #13#10 +
        'Приложение всё равно установлено. Запустите AriaSignature.UI (лучше один раз от администратора) — UI выполнит повторный recovery.'#13#10 +
        'Логи: %ProgramData%\AriaSignature\logs\; проверка службы: services.msc.',
        mbInformation,
        MB_OK,
        IDOK);
      Exit;
    end;
  end;

  Healthy := False;
  for PortAttempt := 1 to 8 do
  begin
    if ProbeLocalApiHealth(5160) then
    begin
      Healthy := True;
      Break;
    end;
    Log(Format('Health probe attempt %d failed with code %d', [PortAttempt, LastProbeExitCode]));
    Sleep(1500);
  end;

  if not Healthy then
  begin
    InstallHealthStatus := 'install-health:fail-with-repair';
    if not RunBootstrapRepair(BinPath) then
    begin
      InstallHealthStatus := 'install-health:degraded-api-not-ready';
      Log('Warning: API health check failed and bootstrap repair also failed; installer will continue with degraded startup path.');
      SuppressibleMsgBox(
        BuildBootstrapFailureHint() + #13#10 +
        'Служба запущена, но API пока не прошёл локальную проверку /api/v1/status.'#13#10 +
        'Установка продолжена: UI подождёт прогрев API и повторит запуск.'#13#10 +
        'Проверьте %ProgramData%\AriaSignature\logs\service-*.log при повторении проблемы.',
        mbInformation,
        MB_OK,
        IDOK);
      Exit;
    end;

    Healthy := ProbeLocalApiHealth(5160);
    if not Healthy then
    begin
      InstallHealthStatus := 'install-health:degraded-api-warmup';
      Log('Warning: API still not ready after auto-repair; installer continues and delegates warmup/retry to UI.');
      SuppressibleMsgBox(
        'API ещё не отвечает после автоматического восстановления, но установка завершена.'#13#10 +
        'Откройте AriaSignature.UI — приложение продолжит ожидание/восстановление автоматически.'#13#10 +
        'Логи: %ProgramData%\AriaSignature\logs\.',
        mbInformation,
        MB_OK,
        IDOK);
      Exit;
    end;
  end;

  InstallHealthStatus := 'install-health:ok';
  Log(InstallHealthStatus);
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

procedure RemoveLegacyCommonStartupShortcutBestEffort();
begin
  if DeleteFile(ExpandConstant('{commonstartup}\AriaSignature.lnk')) then
    Log('Removed legacy common Startup shortcut AriaSignature.lnk');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopAndDeleteServiceBestEffort();
    KillServiceProcessBestEffort();
    if not WaitServiceAbsent(45) then
    begin
      Log('Service still exists before file copy; installer continues and will retry create later.');
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    InstallServiceOrAbort();
    RemoveLegacyCommonStartupShortcutBestEffort();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteTrayLogonTaskBestEffort();
    StopAndDeleteServiceBestEffort();
    KillServiceProcessBestEffort();
  end;
end;
