; ImageResize Context Menu - Inno Setup Script
; This creates a proper Windows installer (.exe)

#ifndef Platform
  #define Platform "x64"
#endif

#define MyAppName "ImageResize Context Menu"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ImageResize"
#define MyAppURL "https://github.com/YodasMyDad/ImageResize"
#define MyAppExeName "ImageResize.ContextMenu.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
AppId={{B8F3E9A1-7C2D-4F5E-9A3B-1E8D6C4A2F7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\ImageResize
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
; Require admin rights for registry modifications
PrivilegesRequired=admin
OutputDir=..\publish\installer
OutputBaseFilename=ImageResize-ContextMenu-{#Platform}
; SetupIconFile=Assets\icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Minimum Windows 10 version 1809
MinVersion=10.0.17763
#if Platform == "x64"
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
#else
ArchitecturesAllowed=x86 x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Install the built application files
Source: "..\publish\{#Platform}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Register context menu for .jpg
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .jpeg
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .png
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .gif
Root: HKCR; Subkey: "SystemFileAssociations\.gif\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.gif\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.gif\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.gif\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .webp
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .bmp
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .tif
Root: HKCR; Subkey: "SystemFileAssociations\.tif\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.tif\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.tif\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.tif\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Register context menu for .tiff
Root: HKCR; Subkey: "SystemFileAssociations\.tiff\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.tiff\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "SystemFileAssociations\.tiff\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "SystemFileAssociations\.tiff\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

; Generic verb for any file type but restricted to pictures via AppliesTo
Root: HKCR; Subkey: "*\shell\ResizeImage"; ValueType: string; ValueName: ""; ValueData: "Resize Images"; Flags: uninsdeletekey
Root: HKCR; Subkey: "*\shell\ResizeImage"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCR; Subkey: "*\shell\ResizeImage"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.Kind:=picture"
Root: HKCR; Subkey: "*\shell\ResizeImage"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\icon.ico"""; Check: FileExists(ExpandConstant('{app}\Assets\icon.ico'))
Root: HKCR; Subkey: "*\shell\ResizeImage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" %*"

[Code]
// Check for .NET 9.0 Runtime
function IsDotNetInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  // Try to run dotnet --version
  Result := Exec('dotnet.exe', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  
  // Check if .NET is installed
  if not IsDotNetInstalled() then
  begin
    if MsgBox('.NET 9.0 Runtime is required but not found.' + #13#10 + #13#10 +
              'Would you like to download it now?' + #13#10 + #13#10 +
              'The installer will open the download page in your browser.', 
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0', '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
    Result := False;
  end;
end;

[UninstallRun]
; Restart Explorer to refresh context menu
Filename: "{cmd}"; Parameters: "/c taskkill /f /im explorer.exe && start explorer.exe"; Flags: runhidden; RunOnceId: "RestartExplorer"
