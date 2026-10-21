; The Daralet installer: copies Daralet.dll and registers it with Decal, so players don't have to Add, Browse in the
; Decal agent. Built with Inno Setup 6 by .github/workflows/release.yml; to build it by hand, after a Release build:
;
;   iscc /DAppVersion=0.4.0 installer\Daralet.iss
;
; It writes Daralet-Setup-<version>.exe to out\. Defines it takes (/Dname=value): AppVersion, BinDir (where Daralet.dll
; is, ..\bin by default) and UBServiceInstaller (UtilityBelt.Service.Installer.exe, from the NuGet package by default).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef BinDir
  #define BinDir "..\bin"
#endif
#ifndef UBServiceInstaller
  ; the UtilityBelt.Service NuGet package carries its own installer; the project only uses the package's lib
  #define NuGetRoot GetEnv("NUGET_PACKAGES")
  #if NuGetRoot == ""
    #define NuGetRoot GetEnv("USERPROFILE") + "\.nuget\packages"
  #endif
  #define UBServiceInstaller NuGetRoot + "\utilitybelt.service\2.2.12\content\UtilityBelt.Service.Installer.exe"
#endif

; the plugin's assembly guid (src/DaraletPlayer/Properties/AssemblyInfo.cs), which Decal knows it by. "{{" is a literal
; brace to Inno.
#define PluginKey "SOFTWARE\Decal\Plugins\{{96f4c651-bc02-41a7-9288-5ef8a5db7d6a}"

[Setup]
; never change: it's how a newer installer finds and upgrades the old install
AppId={{e2f58956-0d15-47ac-898a-6e08059143cf}
AppName=Daralet
AppVersion={#AppVersion}
AppVerName=Daralet {#AppVersion}
AppPublisher=Daralet
AppPublisherURL=https://play.daralet.ac/
AppSupportURL=https://github.com/daralet-ac/daralet-player-plugin
VersionInfoVersion={#AppVersion}
DefaultDirName={code:DefaultInstallDir}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
; Decal's keys are under HKLM
PrivilegesRequired=admin
; 32-bit mode, so HKLM\SOFTWARE is the WOW6432Node one Decal (a 32-bit program) reads
OutputDir=..\out
OutputBaseFilename=Daralet-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Daralet (Decal plugin)
; the game locks Daralet.dll while it runs: Setup offers to close it
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
FinishedLabel=Daralet is installed and ticked under Plugins in the Decal agent.%n%nStart the game through Decal, log in to Daralet and type /dap to open the window.

[Tasks]
Name: "ubservice"; Description: "Install UtilityBelt.Service 2.2.12 (Daralet needs it to draw its window, and it isn't installed)"; Check: NeedsUBService

[Files]
Source: "{#BinDir}\Daralet.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#UBServiceInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: ubservice

[InstallDelete]
; v0.1.0's name for the dll, which Decal would no longer load once the entry below names Daralet.dll
Type: files; Name: "{app}\DaraletPlayer.dll"

[Registry]
; what Decal's own Add writes for a .NET plugin (and UtilityBelt's plugin template installer): Surrogate is Decal's
; .NET plugin host. Written every time, so an entry from adding the dll by hand is taken over and points here.
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: string; ValueName: ""; ValueData: "Daralet"; Flags: uninsdeletekey
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: dword; ValueName: "Enabled"; ValueData: 1
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: string; ValueName: "Object"; ValueData: "DaraletPlayer.PluginCore"
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: string; ValueName: "Assembly"; ValueData: "Daralet.dll"
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"
Root: HKLM32; Subkey: "{#PluginKey}"; ValueType: string; ValueName: "Surrogate"; ValueData: "{{71A69713-6593-47EC-0002-0000000DECA1}"

[Run]
Filename: "{tmp}\UtilityBelt.Service.Installer.exe"; StatusMsg: "Installing UtilityBelt.Service..."; Tasks: ubservice; Flags: waituntilterminated

[Code]
const
  PluginKey = 'SOFTWARE\Decal\Plugins\{96f4c651-bc02-41a7-9288-5ef8a5db7d6a}';
  // UtilityBelt.Service's assembly guid, which Decal lists it under (Services)
  UBServiceKey = 'SOFTWARE\Decal\Services\{8adc5729-db1a-4e28-9475-c4eafae1e6e7}';
  DecalUrl = 'https://www.decaldev.com/';
  DotNetUrl = 'https://dotnet.microsoft.com/download/dotnet-framework/net48';
  // .NET Framework 4.8's lowest Release number (Microsoft's documented check)
  DotNet48Release = 528040;

// Where Decal already loads Daralet from, if it was added by hand, so the new dll replaces that one; else the README's
// suggested folder.
function DefaultInstallDir(Param: String): String;
var
  Path: String;
begin
  if RegQueryStringValue(HKLM32, PluginKey, 'Path', Path) and (Path <> '') and DirExists(Path) then begin
    Result := RemoveBackslashUnlessRoot(Path);
  end else begin
    Result := 'C:\Games\Decal Plugins\Daralet';
  end;
end;

// Installed means Decal lists it and, where the entry says where, the dll is there.
function UBServiceInstalled: Boolean;
var
  Path, Assembly: String;
begin
  Result := RegKeyExists(HKLM32, UBServiceKey);
  if Result and RegQueryStringValue(HKLM32, UBServiceKey, 'Path', Path)
      and RegQueryStringValue(HKLM32, UBServiceKey, 'Assembly', Assembly) then begin
    Result := FileExists(AddBackslash(Path) + Assembly);
  end;
end;

function NeedsUBService: Boolean;
begin
  Result := not UBServiceInstalled;
end;

// Decal's version, from the agent's own file (its title bar shows the same). False if Decal can't be found.
function GetDecalVersion(var MS, LS: Cardinal): Boolean;
var
  Dir: String;
begin
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\Decal\Agent', 'AgentPath', Dir) or (Dir = '') then begin
    Dir := ExpandConstant('{commonpf32}\Decal 3.0');
  end;
  Result := GetVersionNumbers(AddBackslash(Dir) + 'DenAgent.exe', MS, LS)
    or GetVersionNumbers(AddBackslash(Dir) + 'Inject.dll', MS, LS);
end;

function VersionText(MS, LS: Cardinal): String;
begin
  Result := Format('%d.%d.%d.%d', [MS shr 16, MS and $FFFF, LS shr 16, LS and $FFFF]);
end;

// Asks whether to go on; No opens the download page and stops Setup. Silent installs go on.
function ContinueAnyway(Problem, Url: String): Boolean;
var
  ErrorCode: Integer;
begin
  Result := SuppressibleMsgBox(Problem + #13#10#13#10 + 'Yes installs Daralet anyway. No opens ' + Url
    + ' and stops here.', mbConfirmation, MB_YESNO, IDYES) = IDYES;
  if not Result then begin
    ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

// The game or the Decal agent, by WMI, since Inno can't list processes itself. False if WMI can't say.
function IsRunning(ExeName: String): Boolean;
var
  Locator, Wmi, Found: Variant;
begin
  Result := False;
  try
    // Inno's Pascal can't call a method on a function's result directly
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = "' + ExeName + '"');
    Result := Found.Count > 0;
  except
  end;
end;

function InitializeSetup: Boolean;
var
  Release, MS, LS: Cardinal;
begin
  Result := True;

  // the game holds Daralet.dll, and UtilityBelt.Service's installer wants Decal closed
  while Result and (IsRunning('acclient.exe') or IsRunning('DenAgent.exe')) do begin
    Result := SuppressibleMsgBox('Close Asheron''s Call and the Decal agent (its icon in the system tray: right-click, Exit), then click Retry.',
      mbError, MB_RETRYCANCEL, IDCANCEL) = IDRETRY;
  end;
  if not Result then begin
    exit;
  end;

  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
      or (Release < DotNet48Release) then begin
    Result := ContinueAnyway('Daralet needs .NET Framework 4.8, which doesn''t seem to be installed.', DotNetUrl);
    if not Result then begin
      exit;
    end;
  end;

  // Decal 3.0 reports itself as 2.9.7.5 and can't run a .NET 4.8 plugin ("Failed to register plugin")
  if not GetDecalVersion(MS, LS) then begin
    Result := ContinueAnyway('Decal wasn''t found. Daralet is a Decal plugin and needs Decal 2.9.8.3.', DecalUrl);
  end else if (MS < $00020009) or ((MS = $00020009) and (LS < $00080000)) then begin
    Result := ContinueAnyway('Decal ' + VersionText(MS, LS) + ' is installed, and Daralet needs Decal 2.9.8.3 or newer: '
      + 'older ones (including "Decal 3.0", which is 2.9.7.5) can''t run it. Install Decal 2.9.8.3 over it, and the '
      + 'Visual C++ 2017 (x86) runtime it links to. Your other plugins keep working.', DecalUrl);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssDone) and WizardIsTaskSelected('ubservice') and NeedsUBService then begin
    SuppressibleMsgBox('UtilityBelt.Service doesn''t seem to have installed. Daralet can''t open its window without it: '
      + 'see "What you need" at https://github.com/daralet-ac/daralet-player-plugin.', mbError, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    // what the plugin wrote (DataFolder.cs); kept unless asked, since it has the player's loot profiles
    Data := ExpandConstant('{userdocs}\Decal Plugins\Daralet');
    if DirExists(Data) and (SuppressibleMsgBox('Delete your Daralet settings and loot profiles too?' + #13#10#13#10 + Data,
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES) then begin
      DelTree(Data, True, True, True);
    end;
  end;
end;
