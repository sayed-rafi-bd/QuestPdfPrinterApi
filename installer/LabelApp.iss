; Inno Setup script - builds Setup.exe from the output of build-installer.ps1 (..\publish).
; Free download: https://jrsoftware.org/isinfo.php

#define AppName "Shipping Label Printer"
#define AppVersion "1.0.0"
#define AppExe "QuestPdfPrinterApi.exe"
#define Publish "..\publish"

; Fail the build early (instead of shipping a broken installer) if a bundled tool is missing.
#if !FileExists(Publish + "\Tools\SumatraPDF\SumatraPDF.exe")
  #error SumatraPDF.exe is missing - put it in Tools\SumatraPDF and run build-installer.ps1 again.
#endif
#if !FileExists(Publish + "\Tools\Fonts\NotoSansJP-Regular.ttf")
  #error NotoSansJP-Regular.ttf is missing - put it in Tools\Fonts and run build-installer.ps1 again.
#endif

[Setup]
AppId={{6F0B8E52-3C1D-4B7A-9A52-0D5E7A1C4F10}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\ShippingLabelPrinter
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=ShippingLabelPrinter-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\Assets\icon.ico
WizardImageFile=..\Assets\wizard-large.bmp
WizardSmallImageFile=..\Assets\wizard-small.bmp
; Installs per-user by default (no admin prompt). Printers are per-user on Windows, so the app
; must run as the logged-in user to see them - it is NOT installed as a service.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Same name as the mutex in Program.cs - the installer asks to close the app if it's running.
AppMutex=LabelApp.SingleInstance
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "network"; Description: "Allow phones/tablets on my network to use it (no login - only on a network you trust)"; GroupDescription: "Network:"; Flags: unchecked
Name: "autostart"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#Publish}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
; Marker file read by Program.cs: present = listen on all network adapters, not just localhost.
Source: "network.enabled"; DestDir: "{app}"; Flags: ignoreversion; Tasks: network

[InstallDelete]
; Re-running the installer resets the option to whatever was chosen this time.
Type: files; Name: "{app}\network.enabled"

[Icons]
; Opens the UI (starting the app first if it isn't running).
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--open-browser --log-to-file"; WorkingDir: "{app}"
Name: "{group}\Stop {#AppName}"; Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; IconFilename: "{app}\{#AppExe}"; Flags: runminimized
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--open-browser --log-to-file"; WorkingDir: "{app}"; Tasks: desktopicon
; Startup: run in the background without opening a browser tab.
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--log-to-file"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--open-browser --log-to-file"; WorkingDir: "{app}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"
