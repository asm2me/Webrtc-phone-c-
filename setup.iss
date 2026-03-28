[Setup]
AppName=VOIP@ Dialer
AppVersion=2.0
AppPublisher=VOIPAT
AppPublisherURL=https://voipat.com
AppSupportURL=https://voipat.com
DefaultDirName={autopf}\VOIPAT Dialer
DefaultGroupName=VOIPAT Dialer
OutputDir=installer
OutputBaseFilename=VOIPAT-Dialer-Setup-2.0
SetupIconFile=voipat.ico
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\WebRtcPhoneDialer.exe
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Start with Windows"; GroupDescription: "Startup:"

[Files]
Source: "bin\Release\net481\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\VOIP@ Dialer"; Filename: "{app}\WebRtcPhoneDialer.exe"
Name: "{group}\Uninstall VOIP@ Dialer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VOIP@ Dialer"; Filename: "{app}\WebRtcPhoneDialer.exe"; Tasks: desktopicon
Name: "{userstartup}\VOIP@ Dialer"; Filename: "{app}\WebRtcPhoneDialer.exe"; Tasks: startupicon

[Registry]
; Register voipat:// URL protocol so provisioning links open the app
Root: HKCU; Subkey: "Software\Classes\voipat";                    ValueType: string; ValueData: "URL:VOIP@ Dialer Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\voipat";                    ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\voipat\DefaultIcon";        ValueType: string; ValueData: "{app}\WebRtcPhoneDialer.exe,0"
Root: HKCU; Subkey: "Software\Classes\voipat\shell\open\command"; ValueType: string; ValueData: """{app}\WebRtcPhoneDialer.exe"" ""%1"""

[Run]
Filename: "{app}\WebRtcPhoneDialer.exe"; Description: "Launch VOIP@ Dialer"; Flags: nowait postinstall skipifsilent
