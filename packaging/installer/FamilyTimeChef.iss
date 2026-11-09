; Inno Setup script for FamilyTimeChef.
; Build with: packaging\build-portable.ps1
; /DMyVersion and /DMySource are supplied by the packaging script.

#ifndef MyVersion
  #define MyVersion "0.3.0"
#endif
#ifndef MySource
  #define MySource "..\..\dist\FamilyTimeChef-" + MyVersion + "-ep3-dx11-portable"
#endif

[Setup]
AppId={{9F2C6A54-7E1B-4C0D-9E3A-6B2F5D8C1A47}
AppName=FamilyTimeChef
AppVersion={#MyVersion}
AppPublisher=FamilyTimeChef contributors
DefaultDirName={localappdata}\FamilyTimeChef
DefaultGroupName=FamilyTimeChef
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=FamilyTimeChef-{#MyVersion}-ep3-dx11-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=FamilyTimeChef
LicenseFile={#MySource}\LICENSE
InfoBeforeFile={#MySource}\LEGAL.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MySource}\FamilyTimeChef.Studio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\FamilyTimeChef.InGame.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\ftagent.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\ftinject.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\ftinput.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\INSTALL.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\LEGAL.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FamilyTimeChef Studio"; Filename: "{app}\FamilyTimeChef.Studio.exe"
Name: "{group}\Installation notes"; Filename: "{app}\INSTALL.md"
Name: "{group}\Uninstall FamilyTimeChef"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\INSTALL.md"; Description: "Open installation notes"; Flags: postinstall shellexec nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
