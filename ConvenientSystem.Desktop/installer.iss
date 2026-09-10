; ConvenientSystem 桌面客户端安装程序
; 使用 Inno Setup 编译：在本文件所在目录执行 iscc installer.iss（或项目根执行 iscc ConvenientSystem.Desktop\installer.iss）
; 前置条件：先运行 构建安装包.cmd 构建 Vue 前端 + dotnet publish 桌面 exe（产物在项目根 exe\，本脚本用 ..\exe\ 引用）

; 版本号唯一真相 = 编译产物 exe 内嵌的版本信息（由 csproj 的 AssemblyVersion 决定）。
; 这里自动读取，杜绝 iss / csproj / 后台记录三处版本号人工同步不一致导致的更新死循环。
; SourcePath 是本 .iss 文件所在目录，因此无论从哪个目录调用 iscc 都能定位到 exe。
#define AppExeSrc SourcePath + "\..\exe\ConvenientSystem.exe"
#define AppVer GetVersionNumbersString(AppExeSrc)

[Setup]
AppName=ConvenientSystem
AppVersion={#AppVer}
; 让生成的 Setup.exe 自身也带上版本信息，后台上传时据此自动识别真实版本号
VersionInfoVersion={#AppVer}
AppPublisher=ConvenientSystem
DefaultDirName={autopf}\ConvenientSystem
DefaultGroupName=ConvenientSystem
UninstallDisplayIcon={app}\ConvenientSystem.exe
UninstallDisplayName=ConvenientSystem 桌面客户端
OutputDir=..\installer-output
OutputBaseFilename=ConvenientSystem-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
PrivilegesRequired=admin

; 安装到 C:\Program Files\ConvenientSystem 需要管理员权限
; 根目录授予 Users 组写入权限（git-repos.json 等用户数据文件写入安装目录）
; wwwroot 目录授权（运行时下载更新前端），logs 目录授权（运行日志）
[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\wwwroot"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify

[Files]
; 桌面客户端（单文件自包含 exe）
Source: "..\exe\ConvenientSystem.exe"; DestDir: "{app}"; Flags: ignoreversion
; 配置文件（仅在不存在时安装，避免覆盖用户已修改的配置）
Source: "..\exe\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
; 运行时所需的原生依赖（如有则复制）
Source: "..\exe\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; 前端初始版本（首次安装时提供离线可用，后续通过服务器更新）
Source: "..\exe\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; GroupDescription: "附加选项:"

[Icons]
Name: "{group}\ConvenientSystem"; Filename: "{app}\ConvenientSystem.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 ConvenientSystem"; Filename: "{uninstallexe}"
Name: "{commondesktop}\ConvenientSystem"; Filename: "{app}\ConvenientSystem.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ConvenientSystem.exe"; Description: "立即启动 ConvenientSystem"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清理 wwwroot 和 logs（用户数据）
Type: filesandordirs; Name: "{app}\wwwroot"
Type: filesandordirs; Name: "{app}\logs"

[Code]
function InitializeSetup(): Boolean;
var
  ProcessName: string;
  Retries: Integer;
  ResultCode: Integer;
begin
  Result := True;
  ProcessName := 'ConvenientSystem.exe';

  // 安装前强制结束正在运行的 ConvenientSystem.exe 进程，避免文件被占用导致安装失败。
  // 先执行一次 taskkill，然后轮询最多 10 秒等待进程退出。
  // 禁止加 /T（按进程树结束）：更新场景下本安装程序正是 ConvenientSystem.exe
  // 启动出来的子进程，/T 会把自己一并杀掉——表现为“下载完成后没反应”，
  // 安装日志停在 Created protected temporary directory 一行、is-*.tmp 临时目录残留。
  Exec('taskkill.exe', '/F /IM ' + ProcessName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Retries := 0;
  while Retries < 20 do
  begin
    // 通过 tasklist 检查进程是否仍然存在（不显示命令窗口）
    if not Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq ' + ProcessName + '" /NH | find /I "' + ProcessName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      break;
    if ResultCode <> 0 then
      break;

    if Retries = 0 then
      Log('检测到 ' + ProcessName + ' 正在运行，正在等待其退出...');

    Exec('taskkill.exe', '/F /IM ' + ProcessName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    Inc(Retries);
  end;
end;
