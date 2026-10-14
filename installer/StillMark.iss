; Still mark installer (Inno Setup 6). Build it with tools\build-installer.ps1, which publishes the program into a
; staging folder and passes it in as SourceDir together with AppVersion.
;
; - Installs for the current user by default (no administrator rights); "Install for all users" is offered too.
; - The installed program keeps settings and drafts in %LOCALAPPDATA%\StillMark (no portable.txt is installed),
;   so updating or uninstalling never touches them.
; - The optional "Open with" task registers Still mark for Markdown files; Windows still lets the user choose the
;   default app (programs cannot set it themselves).

#ifndef SourceDir
  #error SourceDir is not defined: build the installer with tools\build-installer.ps1
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{37DAF957-2D8D-4FF5-8988-C8A1740E6DBB}
AppName=Still mark
AppVersion={#AppVersion}
AppVerName=Still mark {#AppVersion}
AppPublisher=Le inner
AppCopyright=Copyright © 2026 Le inner
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Still mark
VersionInfoDescription=Still mark Setup
DefaultDirName={autopf}\StillMark
DefaultGroupName=Still mark
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\src\StillMark\Assets\app.ico
UninstallDisplayIcon={app}\StillMark.exe
UninstallDisplayName=Still mark
OutputDir=..\dist\installer
OutputBaseFilename=StillMark-{#AppVersion}-setup
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
en.OpenWithTask=Add Still mark to "Open with" for .md / .markdown files
zhcn.OpenWithTask=把 Still mark 加入 .md / .markdown 文件的“打开方式”
en.FileTypeName=Markdown document
zhcn.FileTypeName=Markdown 文档
en.AppDescription=Offline Markdown reader and editor
zhcn.AppDescription=离线 Markdown 阅读与编辑器

[Messages]
zhcn.WelcomeLabel1=欢迎使用 Still mark 墨 安装向导

[Tasks]
Name: "openwith"; Description: "{cm:OpenWithTask}"; GroupDescription: "{cm:OpenWithGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Still mark"; Filename: "{app}\StillMark.exe"
Name: "{autodesktop}\Still mark"; Filename: "{app}\StillMark.exe"; Tasks: desktopicon

[Registry]
; File type (ProgID) used by "Open with" and by the Default apps settings page.
Root: HKA; Subkey: "Software\Classes\StillMark.md"; ValueType: string; ValueName: ""; ValueData: "{cm:FileTypeName}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\StillMark.md\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\StillMark.exe,0"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\StillMark.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\StillMark.exe"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "StillMark.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "StillMark.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "StillMark.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: string; ValueName: "StillMark.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
; The application itself, for "Open with" lists.
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Still mark"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe\SupportedTypes"; ValueType: string; ValueName: ".mdown"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe\SupportedTypes"; ValueType: string; ValueName: ".mkd"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\StillMark.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\StillMark.exe"" ""%1"""; Tasks: openwith
; Capabilities: lists Still mark under Settings > Apps > Default apps.
Root: HKA; Subkey: "Software\StillMark"; Flags: uninsdeletekeyifempty; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Still mark"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "StillMark.md"; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "StillMark.md"; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "StillMark.md"; Tasks: openwith
Root: HKA; Subkey: "Software\StillMark\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "StillMark.md"; Tasks: openwith
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "StillMark"; ValueData: "Software\StillMark\Capabilities"; Flags: uninsdeletevalue; Tasks: openwith

[Run]
Filename: "{app}\StillMark.exe"; Description: "{cm:LaunchProgram,Still mark}"; Flags: nowait postinstall skipifsilent
