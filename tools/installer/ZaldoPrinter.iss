[Setup]
AppId={{E3D4B4AE-2A38-4C31-A78A-3F8E3E4AE511}
AppName=Zaldo Printer
AppVersion=1.0.0
AppPublisher=Zaldo
DefaultDirName={autopf}\ZaldoPrinter
DefaultGroupName=Zaldo Printer
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\ZaldoPrinter.ConfigApp.exe
OutputDir=..\dist
OutputBaseFilename=ZaldoPrinterSetup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern

[Files]
Source: "..\dist\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
function CanLaunchConfigApp: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\ZaldoPrinter.ConfigApp.exe'));
end;

[Icons]
Name: "{autoprograms}\Zaldo Printer\Zaldo Printer Config"; Filename: "{app}\ZaldoPrinter.ConfigApp.exe"; Check: CanLaunchConfigApp
Name: "{autodesktop}\Zaldo Printer Config"; Filename: "{app}\ZaldoPrinter.ConfigApp.exe"; Tasks: desktopicon; Check: CanLaunchConfigApp

[Tasks]
Name: "desktopicon"; Description: "Criar atalho no desktop"; GroupDescription: "Atalhos:"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop ""ZaldoPrinterService"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create ""ZaldoPrinterService"" binPath= ""{app}\ZaldoPrinter.Service.exe"" start= auto DisplayName= ""Zaldo Printer Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "config ""ZaldoPrinterService"" binPath= ""{app}\ZaldoPrinter.Service.exe"" start= auto DisplayName= ""Zaldo Printer Service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description ""ZaldoPrinterService"" ""Zaldo Printer local API and thermal print service"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ""ZaldoPrinterService"""; Flags: runhidden
Filename: "{app}\ZaldoPrinter.ConfigApp.exe"; Description: "Abrir Zaldo Printer Config"; Flags: nowait postinstall skipifsilent skipifdoesntexist; Check: CanLaunchConfigApp

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""ZaldoPrinterService"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""ZaldoPrinterService"""; Flags: runhidden
