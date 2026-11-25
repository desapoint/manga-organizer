[Setup]
AppId={{18826A88-F7BC-48AB-9AA5-64EE67683D2D}
AppName=Manga Organizer
AppVersion={#ReleaseVersion}
AppPublisher=Manga Organizer
AppPublisherURL=https://github.com/desapoint/manga-organizer
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Manga Organizer
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MangaOrganizer.Server.exe
OutputBaseFilename=MangaOrganizer-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Manga Organizer"; Filename: "{app}\MangaOrganizer.Server.exe"; WorkingDir: "{app}"
