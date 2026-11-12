#ifndef Arch
  #error Arch must be passed from the packaging workflow
#endif
#ifndef AppVersion
  #error AppVersion must be passed from the packaging workflow
#endif
#ifndef SourceDir
  #error SourceDir must be passed from the packaging workflow
#endif
[Setup]
AppId={{CF6AAB62-DA00-49AF-B3D2-1DD83FF90143}
AppName=Najm Card New
AppVersion={#AppVersion}
AppPublisher=Najm Card New
DefaultDirName={localappdata}\Programs\NajmCardNew
DefaultGroupName=Najm Card New
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\src\NajmCard.Desktop\Assets\NajmCard.ico
UninstallDisplayIcon={app}\NajmCard.exe
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "الاختصارات:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Najm Card New"; Filename: "{app}\NajmCard.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Najm Card New"; Filename: "{app}\NajmCard.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\NajmCard.exe"; Description: "Launch Najm Card New"; Flags: nowait postinstall skipifsilent
