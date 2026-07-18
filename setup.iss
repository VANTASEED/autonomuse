[Setup]
AppName=Autonomuse
AppVerName=Autonomuse
AppVersion=1.0.0-launch
AppPublisher=VantaSeed
AppId={{B0F1A2E3-4D5C-6B7A-8F9E-0D1C2B3A4F5E}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={autopf}\Autonomuse
DefaultGroupName=Autonomuse
OutputDir=.
OutputBaseFilename=Autonomuse_Setup
SetupIconFile=wwwroot\images\Assets\AutonomuseIcon.ico
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\Autonomuse.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Autonomuse"; Filename: "{app}\Autonomuse.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Autonomuse"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Autonomuse"; Filename: "{app}\Autonomuse.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Autonomuse.exe"; Description: "Launch Autonomuse"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent
