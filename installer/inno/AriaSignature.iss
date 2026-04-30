; AriaSignature installer script (Inno Setup 6)
; Build binaries first, then run this script in Inno Setup Compiler.

#define MyAppName "AriaSignature"
#define MyAppVersion "0.1.0"
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
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\ui\{#MyAppExeName}
; SetupIconFile can be enabled after providing a multi-size valid ICO.

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

[Icons]
Name: "{group}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{autodesktop}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\ui\Assets\icon.ico"
Name: "{commonstartup}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; Parameters: "--tray"; Tasks: autostarttray; IconFilename: "{app}\ui\Assets\icon.ico"

[Run]
Filename: "{app}\ui\{#MyAppExeName}"; Parameters: "--tray"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall skipifsilent

[UninstallRun]

[Code]
const
  ServiceName = 'AriaSignatureService';
  SC_ACCEPTABLE_NOT_FOUND = 1060;
  SC_ACCEPTABLE_NOT_ACTIVE = 1062;
  SC_ACCEPTABLE_ALREADY_RUNNING = 1056;

function ExecSc(const Params: string; const AcceptableCodeA: Integer; const AcceptableCodeB: Integer): Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if not Result then
  begin
    Log(Format('Failed to execute sc.exe with params: %s', [Params]));
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

procedure StopAndDeleteServiceBestEffort();
begin
  ExecSc(Format('stop %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_ACTIVE);
  ExecSc(Format('delete %s', [ServiceName]), 0, SC_ACCEPTABLE_NOT_FOUND);
end;

procedure InstallServiceOrAbort();
var
  BinPath: string;
begin
  StopAndDeleteServiceBestEffort();
  BinPath := ExpandConstant('{app}\service\{#MyServiceExeName}');

  if not ExecSc(Format('create %s binPath= "%s" start= auto', [ServiceName, BinPath]), 0, -1) then
  begin
    RaiseException('Не удалось зарегистрировать службу AriaSignatureService');
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
  end;
end;
