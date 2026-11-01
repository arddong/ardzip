; arddong ArdZip 설치 파일 = 사용자 권한 설치 + 오른쪽 클릭 메뉴(압축 풀기·zip 으로 압축) + 연결 프로그램 등록
#define AppVer "1.0.0"
#define Exts "zip 7z rar tar gz tgz bz2 tbz2 xz txz zst alz egg"

[Setup]
AppId={{CD2E76D9-6E1B-4C31-983C-0AFE28352121}
AppName=ArdZip
AppVersion={#AppVer}
AppPublisher=arddong
DefaultDirName={autopf}\ArdZip
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=dist
OutputBaseFilename=ArdZip_Setup_{#AppVer}
SetupIconFile=src\ardzip.ico
UninstallDisplayIcon={app}\ArdZip.exe
UninstallDisplayName=ArdZip
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
Source: "publish\ArdZip.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ArdZip"; Filename: "{app}\ArdZip.exe"

[Registry]
; 압축 파일 오른쪽 클릭 = 압축 풀기
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "압축 풀기"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\ArdZip.exe,0"
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Extract"; ValueType: string; ValueName: "AppliesTo"; ValueData: "{code:AppliesTo}"
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArdZip.exe"" /x ""%1"""

; 파일·폴더 오른쪽 클릭 = zip 으로 압축
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Compress"; ValueType: string; ValueName: "MUIVerb"; ValueData: "zip 으로 압축"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Compress"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\ArdZip.exe,0"
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Compress"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\*\shell\ArdZip.Compress\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArdZip.exe"" /a ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ArdZip.Compress"; ValueType: string; ValueName: "MUIVerb"; ValueData: "zip 으로 압축"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ArdZip.Compress"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\ArdZip.exe,0"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ArdZip.Compress"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ArdZip.Compress\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArdZip.exe"" /a ""%1"""

; 두 번 눌러 열 프로그램 후보로 올린다(기본 지정은 윈도우가 사용자에게만 허락한다)
Root: HKCU; Subkey: "Software\Classes\ArdZip.Archive"; ValueType: string; ValueName: ""; ValueData: "압축 파일"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ArdZip.Archive\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ArdZip.exe,0"
Root: HKCU; Subkey: "Software\Classes\ArdZip.Archive\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ArdZip.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.zip\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.7z\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.rar\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.tar\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.gz\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.tgz\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.alz\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.egg\OpenWithProgids"; ValueType: string; ValueName: "ArdZip.Archive"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ArdZip.exe"; Description: "ArdZip 열기"; Flags: nowait postinstall skipifsilent

[Code]
function AppliesTo(Param: String): String;
var
  Rest, Ext: String;
  P: Integer;
begin
  Result := '';
  Rest := '{#Exts}';
  while Rest <> '' do
  begin
    P := Pos(' ', Rest);
    if P = 0 then begin Ext := Rest; Rest := ''; end
    else begin Ext := Copy(Rest, 1, P - 1); Rest := Copy(Rest, P + 1, Length(Rest)); end;
    if Result <> '' then Result := Result + ' OR ';
    Result := Result + 'System.FileExtension:=.' + Ext;
  end;
end;
