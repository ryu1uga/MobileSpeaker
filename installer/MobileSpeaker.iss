; Instalador de MobileSpeaker (Inno Setup 6).
; Se compila con build-installer.ps1, que primero publica la aplicacion.

#define AppName "MobileSpeaker"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "MobileSpeaker.exe"
#ifndef PublishDir
  #define PublishDir "..\publish\installer"
#endif
#define FirewallRule "MobileSpeaker"

[Setup]
AppId={{6E1A4C7B-3F52-4D9E-9B1A-5C2E8F7D4A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=MobileSpeaker-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393
; Se necesita administrador para instalar en Program Files y crear la regla del firewall.
PrivilegesRequired=admin
; Detecta si el programa esta abierto (mismo nombre que en Program.cs).
AppMutex=MobileSpeakerSingleInstance
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "firewallprivateonly"; Description: "Permitir conexiones solo en redes privadas (mas restrictivo; puede bloquear al celular si la red de la PC esta marcada como publica)"; GroupDescription: "Firewall de Windows:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Regla de entrada para que el celular pueda conectarse sin el aviso del firewall.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden waituntilterminated; StatusMsg: "Configurando el Firewall de Windows..."
; Por defecto: cualquier perfil de red, pero solo desde la red local (remoteip=localsubnet).
; Asi funciona aunque Windows marque la red (por ejemplo, la de Ethernet) como publica.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=any remoteip=localsubnet"; Flags: runhidden waituntilterminated; Tasks: not firewallprivateonly
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=private remoteip=localsubnet"; Flags: runhidden waituntilterminated; Tasks: firewallprivateonly
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallRule"

[Registry]
; Quita la opcion "Iniciar con Windows" al desinstalar (la crea el programa, no el instalador).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MobileSpeaker"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\MobileSpeaker"
