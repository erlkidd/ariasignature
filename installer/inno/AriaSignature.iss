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
; SetupIconFile can point to icon.ico once converted from root icon.png.

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

[Icons]
Name: "{group}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"
Name: "{autodesktop}\AriaSignature"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "sc.exe"; Parameters: "create AriaSignatureService binPath= ""{app}\service\{#MyServiceExeName}"" start= auto"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start AriaSignatureService"; Flags: runhidden

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop AriaSignatureService"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete AriaSignatureService"; Flags: runhidden
