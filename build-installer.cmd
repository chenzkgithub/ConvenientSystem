@echo off
REM 本文件必须保存为 ANSI/GBK 编码（中文 Windows cmd 的原生编码），且不要加 BOM。
REM 原因：cmd + chcp 65001 读 UTF-8 中文批处理会发生字节漂移，把注释/半截文本当命令执行；
REM 转 GBK 后此问题整族消除。若需编辑本文件，改完请确保存回 ANSI/GBK 编码。
title 构建安装程序 - ConvenientSystem 桌面客户端
setlocal
set "ROOT=%~dp0"
cd /d "%ROOT%"

set "LOG=%ROOT%build-installer.log"
echo ======================================== > "%LOG%"
echo   ConvenientSystem 安装程序构建        >> "%LOG%"
echo   %date% %time%                          >> "%LOG%"
echo ======================================== >> "%LOG%"
echo.

echo ========================================
echo   ConvenientSystem 安装程序构建
echo   1. 构建 Vue 前端
echo   2. 发布桌面客户端（单文件自包含）
echo   3. 编译 Inno Setup 安装程序
echo.
echo   用法：build-installer.cmd [版本号]
echo   - 不传版本号：自动取当前版本 +0.0.1 递增
echo   - 传版本号  ：如 build-installer.cmd 1.1.0 或 1.0.0.3（四段）
echo   版本号会同步写入 csproj / appsettings.json / installer.iss，
echo   上传服务器时请填写脚本输出的版本号，避免更新死循环
echo ========================================
echo.

echo [0/3] 确定版本号并同步（csproj / appsettings.json / installer.iss）...
echo [0/3] 确定版本号并同步（csproj / appsettings.json / installer.iss）... >> "%LOG%"
REM 版本号单点真相源 = csproj 的 Version；未传参则 patch +1，同步写三处后输出。
REM 正则用 \x22 表示双引号，避免 cmd 双引号包裹的命令串里出现裸引号导致解析错乱。
for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$v='%~1'; if(!$v){$m=[regex]::Match([IO.File]::ReadAllText('ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj'),'<Version>([\d\.]+)</Version>'); $p=$m.Groups[1].Value.Split('.'); $p[2]=[int]$p[2]+1; $v=$p -join '.'}; if($v -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){exit 1}; $e=New-Object Text.UTF8Encoding($false); $f='ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'<(Version|FileVersion|AssemblyVersion)>[\d\.]+</(Version|FileVersion|AssemblyVersion)>','<$1>'+$v+'</$1>'),$e); $q=[char]34; $f='ConvenientSystem.Desktop\appsettings.json'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'\x22DesktopVersion\x22\s*:\s*\x22[\d\.]+\x22',$q+'DesktopVersion'+$q+': '+$q+$v+$q),$e); $f='ConvenientSystem.Desktop\installer.iss'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'AppVersion=[\d\.]+','AppVersion='+$v),$e); Write-Output $v"`) do set "APPVER=%%V"
if not defined APPVER (
    echo   [错误] 版本号确定失败（格式应为 x.y.z 或 x.y.z.w，如 1.0.3 / 1.0.0.3）
    echo   [错误] 版本号确定失败 >> "%LOG%"
    goto :failed
)
echo   - 本次构建版本：%APPVER%（上传管理页面时请填这个版本号）
echo   - 本次构建版本：%APPVER% >> "%LOG%"
echo.

echo [1/3] 构建 Vue 前端...
echo [1/3] 构建 Vue 前端... >> "%LOG%"
cd web
REM 提升 Node 堆内存上限，避免 Vite 构建大项目时触发 OOM（Fatal process out of memory: Zone）
set "NODE_OPTIONS=--max-old-space-size=4096"
if not exist "node_modules" (
    echo   - 依赖缺失，执行 npm ci ...
    echo   - 依赖缺失，执行 npm ci ... >> "%LOG%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm ci 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
    if errorlevel 1 goto :failed
) else (
    if not exist "node_modules\vite" (
        echo   - 依赖不完整，执行 npm install 补全 ...
        echo   - 依赖不完整，执行 npm install 补全 ... >> "%LOG%"
        powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm install 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
        if errorlevel 1 goto :failed
    )
)
echo   - 执行 npm run build ...
echo   - 执行 npm run build ... >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm run build 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
set "NODE_OPTIONS="
cd ..
echo   - Vue 前端构建成功
echo   - Vue 前端构建成功 >> "%LOG%"
echo.

echo [2/3] 发布桌面客户端到 exe\...
echo [2/3] 发布桌面客户端到 exe\... >> "%LOG%"
REM 发布前先结束可能正在运行的旧 exe，避免文件被占用导致 dotnet publish 失败
echo   - 尝试结束 ConvenientSystem.exe 进程...
echo   - 尝试结束 ConvenientSystem.exe 进程... >> "%LOG%"
taskkill /F /IM ConvenientSystem.exe /T >> "%LOG%" 2>&1
REM 等待进程释放文件句柄
ping -n 3 127.0.0.1 >nul
if exist "exe" rd /s /q "exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; dotnet publish 'ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SkipVueBuild=true -o 'exe' --nologo -v n 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
echo   - 桌面客户端发布成功
echo   - 桌面客户端发布成功 >> "%LOG%"
echo.

echo [3/3] 编译 Inno Setup 安装程序...
echo [3/3] 编译 Inno Setup 安装程序... >> "%LOG%"
set "ISCC=iscc"
where iscc >nul 2>&1
if not errorlevel 1 goto :found_iscc

REM 依次检测 Inno Setup 6/7 安装路径
for %%P in (
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
    "C:\Program Files\Inno Setup 6\iscc.exe"
    "C:\Program Files\Inno Setup 7\iscc.exe"
    "C:\Program Files (x86)\Inno Setup 7\iscc.exe"
    "D:\innosetup\Inno Setup 6\iscc.exe"
) do (
    if exist %%P (
        set "ISCC=%%~P"
        goto :found_iscc
    )
)
echo   [错误] 未找到 Inno Setup，请安装后重试：https://jrsoftware.org/isdl.php
echo   [错误] 未找到 Inno Setup，请安装后重试：https://jrsoftware.org/isdl.php >> "%LOG%"
goto :failed
:found_iscc
echo   - 使用 ISCC: %ISCC%
echo   - 使用 ISCC: %ISCC% >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; & '%ISCC%' 'ConvenientSystem.Desktop\installer.iss' 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
echo   - 安装程序编译成功
echo   - 安装程序编译成功 >> "%LOG%"
echo.

echo ========================================
echo   安装程序构建完成！
echo   输出文件：installer-output\ConvenientSystem-Setup.exe
echo   版本号：%APPVER%（上传到系统版本管理时必须填这个号，填错会导致客户端更新死循环）
echo   安装路径：C:\Program Files\ConvenientSystem
echo   启动方式：安装后从开始菜单或桌面快捷方式启动
echo   完整日志：%LOG%
echo ========================================
echo.
echo [成功] 构建完成，正在启动安装程序...
echo [成功] 构建完成，正在启动安装程序... >> "%LOG%"
start "" "%ROOT%installer-output\ConvenientSystem-Setup.exe"
echo.
echo 安装程序已启动，本窗口将在 3 秒后自动关闭...
ping -n 4 127.0.0.1 >nul
exit /b 0

:failed
echo.
echo [失败] 构建出错，请查看上方日志或文件：
echo   %LOG%
echo.
echo 按任意键关闭窗口...
pause >nul
exit /b 1
