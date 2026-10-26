#define AppVersion "1.1.0"
[Setup]
AppId={{E83A68A1-EC40-42C3-A2FB-713A581B79B5}
AppName=JustVID
AppVersion={#AppVersion}
AppPublisher={cm:PublisherName}
DefaultDirName={localappdata}\Programs\JustVID
DefaultGroupName=JustVID
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=JustVID-Setup
SetupIconFile=..\JustVID\Assets\JustVID.ico
UninstallDisplayIcon={app}\JustVID.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
[CustomMessages]
english.PublisherName=Minhyeok Cho
korean.PublisherName=조민혁
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\JustVID"; Filename: "{app}\JustVID.exe"
Name: "{autodesktop}\JustVID"; Filename: "{app}\JustVID.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\JustVID.exe"; Description: "JustVID"; Flags: nowait postinstall skipifsilent
