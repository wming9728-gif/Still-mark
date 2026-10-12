; InkPage installer (Inno Setup 6). Build it with tools\build-installer.ps1, which publishes the program into a
; staging folder and passes it in as SourceDir together with AppVersion.
;
; - Installs for the current user by default (no administrator rights); "Install for all users" is offered too.
; - The installed program keeps settings and drafts in %LOCALAPPDATA%\InkPage (no portable.txt is installed),
;   so updating or uninstalling never touches them.
; - The optional "Open with" task registers InkPage for Markdown files; Windows still lets the user choose the
;   default app (programs cannot set it themselves).

#ifndef SourceDir
  #error SourceDir is not defined: build the installer with tools\build-installer.ps1
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{A9CD3C68-988D-472F-9622-2D3F580C7BF6}
AppName=InkPage
AppVersion={#AppVersion}
AppVerName=InkPage {#AppVersion}
AppPublisher=InkPage contributors
VersionInfoVersion={#AppVersion}
VersionInfoProductName=InkPage
VersionInfoDescription=InkPage Setup
DefaultDirName={autopf}\InkPage
DefaultGroupName=InkPage
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\src\InkPage\Assets\app.ico
UninstallDisplayIcon={app}\InkPage.exe
UninstallDisplayName=InkPage
OutputDir=..\dist\installer
OutputBaseFilename=InkPage-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
en.OpenWithGroup=Markdown files:
zhcn.OpenWithGroup=Markdown 文件：
en.OpenWithTask=Add InkPage to "Open with" for .md / .markdown files
zhcn.OpenWithTask=把 InkPage 加入 .md / .markdown 文件的“打开方式”
en.FileTypeName=Markdown document
zhcn.FileTypeName=Markdown 文档
en.AppDescription=Offline Markdown reader and editor
zhcn.AppDescription=离线 Markdown 阅读与编辑器

[Tasks]
Name: "openwith"; Description: "{cm:OpenWithTask}"; GroupDescription: "{cm:OpenWithGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\InkPage"; Filename: "{app}\InkPage.exe"
Name: "{autodesktop}\InkPage"; Filename: "{app}\InkPage.exe"; Tasks: desktopicon

[Registry]
; File type (ProgID) used by "Open with" and by the Default apps settings page.
Root: HKA; Subkey: "Software\Classes\InkPage.md"; ValueType: string; ValueName: ""; ValueData: "{cm:FileTypeName}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\InkPage.md\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\InkPage.exe,0"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\InkPage.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\InkPage.exe"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "InkPage.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "InkPage.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "InkPage.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: string; ValueName: "InkPage.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
; The application itself, for "Open with" lists.
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "InkPage"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe\SupportedTypes"; ValueType: string; ValueName: ".mdown"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe\SupportedTypes"; ValueType: string; ValueName: ".mkd"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\InkPage.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\InkPage.exe"" ""%1"""; Tasks: openwith
; Capabilities: lists InkPage under Settings > Apps > Default apps.
Root: HKA; Subkey: "Software\InkPage"; Flags: uninsdeletekeyifempty; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "InkPage"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "InkPage.md"; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "InkPage.md"; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "InkPage.md"; Tasks: openwith
Root: HKA; Subkey: "Software\InkPage\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "InkPage.md"; Tasks: openwith
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "InkPage"; ValueData: "Software\InkPage\Capabilities"; Flags: uninsdeletevalue; Tasks: openwith

[Run]
Filename: "{app}\InkPage.exe"; Description: "{cm:LaunchProgram,InkPage}"; Flags: nowait postinstall skipifsilent
