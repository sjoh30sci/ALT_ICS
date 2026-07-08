; ALT_ICS — Alternative Internet Connection Sharing
; Inno Setup installer script

#define MyAppName "ALT_ICS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "sjoh30sci"
#define MyAppURL "https://github.com/sjoh30sci/ALT_ICS"
#define MyAppExeName "ALT_ICS.GUI.exe"
#define MyServiceName "ALT_ICS"

[Setup]
AppId={{B8F4A3D2-5E6F-4A7B-9C8D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=.
OutputBaseFilename=ALT_ICS_Setup_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
; SetupIconFile=..\ALT_ICS.GUI\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
; Service binaries
Source: "..\ALT_ICS.Service\bin\Release\net8.0\publish\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
; GUI binaries  
Source: "..\ALT_ICS.GUI\bin\Release\net8.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Shared library
Source: "..\ALT_ICS.Shared\bin\Release\net8.0\*"; DestDir: "{app}\shared"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Configuration (already included in Service publish and GUI bin directories)

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#MyAppName} Dashboard"; Filename: "{app}\{#MyAppExeName}"; Parameters: "dashboard"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Install the Windows Service
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""{app}\service\ALT_ICS.Service.exe"" start= auto DisplayName= ""{#MyAppName} -- Alternative Internet Connection Sharing"""; Flags: runhidden runascurrentuser; StatusMsg: "Installing ALT_ICS Windows Service..."
Filename: "{sys}\sc.exe"; Parameters: "description ""{#MyServiceName}"" ""Custom NAT-based internet connection sharing replacing Windows ICS"""; Flags: runhidden runascurrentuser
Filename: "{sys}\sc.exe"; Parameters: "failure ""{#MyServiceName}"" reset= 86400 actions= restart/60000/restart/120000/restart/300000"; Flags: runhidden runascurrentuser

; Add firewall rules
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ALT_ICS - SignalR"" dir=in action=allow protocol=TCP localport=51000 profile=private description=""ALT_ICS SignalR control channel"""; Flags: runhidden runascurrentuser; StatusMsg: "Configuring Windows Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ALT_ICS - Health"" dir=in action=allow protocol=TCP localport=51001 profile=private description=""ALT_ICS health endpoint"""; Flags: runhidden runascurrentuser
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ALT_ICS - DHCP"" dir=in action=allow protocol=UDP localport=67 profile=private description=""ALT_ICS DHCP server"""; Flags: runhidden runascurrentuser
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ALT_ICS - DNS"" dir=in action=allow protocol=UDP localport=53 profile=private description=""ALT_ICS DNS relay"""; Flags: runhidden runascurrentuser

; Enable IP forwarding (routing)
Filename: "{sys}\reg.exe"; Parameters: "add HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters /v IPEnableRouter /t REG_DWORD /d 1 /f"; Flags: runhidden runascurrentuser; StatusMsg: "Enabling IP forwarding..."

; Start the service
Filename: "{sys}\sc.exe"; Parameters: "start ""{#MyServiceName}"""; Flags: runhidden runascurrentuser; StatusMsg: "Starting ALT_ICS service..."

; Launch the GUI after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ALT_ICS"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; Stop and remove the service
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden runascurrentuser; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden runascurrentuser; RunOnceId: "DeleteService"

; Remove firewall rules
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ALT_ICS - SignalR"""; Flags: runhidden runascurrentuser; RunOnceId: "RemoveFirewall1"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ALT_ICS - Health"""; Flags: runhidden runascurrentuser; RunOnceId: "RemoveFirewall2"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ALT_ICS - DHCP"""; Flags: runhidden runascurrentuser; RunOnceId: "RemoveFirewall3"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ALT_ICS - DNS"""; Flags: runhidden runascurrentuser; RunOnceId: "RemoveFirewall4"

[Code]
function IsDotNet8Installed: Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.0',
    'Version', Version);
  if not Result then
    Result := RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.0',
      'Version', Version);
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  if not IsDotNet8Installed then
  begin
    if MsgBox('ALT_ICS requires .NET 8 Runtime. Would you like to download and install it now?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime',
        '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
    Result := True; // Continue anyway - user can install .NET later
  end
  else
    Result := True;
end;
