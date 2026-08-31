@echo off
chcp 65001 >nul
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
echo ========================================
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
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; & '%ISCC%' 'installer\installer.iss' 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
echo   - 安装程序编译成功
echo   - 安装程序编译成功 >> "%LOG%"
echo.

echo ========================================
echo   安装程序构建完成！
echo   输出文件：installer-output\ConvenientSystem-Setup.exe
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
