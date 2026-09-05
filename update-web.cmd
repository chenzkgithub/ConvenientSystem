@echo off
chcp 65001 >nul
title 更新 Web 前端 - ConvenientSystem 桌面客户端
setlocal
set "ROOT=%~dp0"
cd /d "%ROOT%"

REM 目标安装目录：优先取参数；否则依次探测（注册表卸载信息 → 运行中进程路径 → 常见盘符）
set "TARGET=%~1"
if not "%TARGET%"=="" goto :target_ok

REM ① Inno Setup 安装时写入的卸载注册表（InstallLocation 指向真实安装目录，含自定义盘符）
for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ConvenientSystem_is1" /v InstallLocation 2^>nul ^| findstr InstallLocation') do set "TARGET=%%B"
if not "%TARGET%"=="" goto :target_ok

REM ② 程序正在运行时：取进程可执行文件所在目录（最直接，免注册表）
for /f "usebackq delims=" %%Q in (`powershell -NoProfile -Command "(Get-Process ConvenientSystem -ErrorAction SilentlyContinue | Select-Object -First 1).Path"`) do set "TARGET=%%~dpQ"
if not "%TARGET%"=="" goto :target_ok

REM ③ 常见盘符扫描（安装时允许自定义目录，C/D/E 盘 Program Files 依次尝试）
for %%D in (C D E F) do (
    if exist "%%D:\Program Files\ConvenientSystem\ConvenientSystem.exe" set "TARGET=%%D:\Program Files\ConvenientSystem"
)
if "%TARGET%"=="" set "TARGET=%ProgramFiles%\ConvenientSystem"

:target_ok
REM 去除可能的尾部反斜杠，统一拼路径
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"

REM 覆盖 Program Files 需要管理员权限：不足时自动提权重启自身
net session >nul 2>&1
if errorlevel 1 (
    echo 需要管理员权限（写入 %TARGET%），正在请求提权...
    powershell -NoProfile -Command "Start-Process -FilePath 'cmd.exe' -ArgumentList '/c \"\"%~f0\" \"%TARGET%\"\"' -Verb RunAs"
    exit /b 0
)

echo ========================================
echo   ConvenientSystem Web 前端热更新
echo   目标目录：%TARGET%\wwwroot
echo ========================================
echo.

echo [1/2] 构建 Vue 前端...
cd web
REM 提升 Node 堆内存上限，避免 Vite 构建大项目时触发 OOM（Fatal process out of memory: Zone）
set "NODE_OPTIONS=--max-old-space-size=4096"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm run build 2>&1 | ForEach-Object { $_.ToString() }; exit $LastExitCode"
if errorlevel 1 goto :failed
set "NODE_OPTIONS="
cd ..
echo   - Vue 前端构建成功
echo.

echo [2/2] 覆盖前端文件到安装目录...
if not exist "%TARGET%\ConvenientSystem.exe" (
    echo   [错误] 未找到已安装程序：%TARGET%\ConvenientSystem.exe
    echo   请确认安装目录，或通过参数指定：update-web.cmd "目录路径"
    goto :failed
)

REM 结束运行中的程序（wwwroot 静态文件即改即生效，无需重装）
REM /T 连同 WebView2 子进程（msedgewebview2.exe）一起杀：只杀主进程时子进程会成为孤儿，
REM 继续持有安装目录下 WebView2 用户数据目录的锁，脚本拉起的新实例 WebView2 初始化异常，
REM 页面显示“找不到此页”，需手动关闭重开才恢复；等子进程退净后再启动即可避免
taskkill /F /T /IM ConvenientSystem.exe >nul 2>&1

REM 等待进程树退净（最多 10 秒），再留 2 秒缓冲给个别残留的 WebView2 子进程自行退出
set /a KILL_WAIT=0
:wait_exit
tasklist /FI "IMAGENAME eq ConvenientSystem.exe" 2>nul | find /I "ConvenientSystem.exe" >nul
if not errorlevel 1 if %KILL_WAIT% LSS 10 (
    set /a KILL_WAIT+=1
    timeout /t 1 /nobreak >nul
    goto :wait_exit
)
timeout /t 2 /nobreak >nul

REM robocopy /E 增量覆盖且不删除目标多余文件：
REM 安装目录的 version.json 得以保留，下次启动不会误判为旧版本而被服务器包覆盖回去
robocopy "ConvenientSystem.Desktop\wwwroot" "%TARGET%\wwwroot" /E /NFL /NDL /NJH /NJS /NP
REM robocopy 退出码 0-7 均为成功（1=有文件复制），>=8 才是失败
if errorlevel 8 goto :failed
echo   - 前端文件已覆盖完成（version.json 保留，版本号不变）
echo.

echo ========================================
echo   更新完成！正在启动程序...
echo ========================================
start "" "%TARGET%\ConvenientSystem.exe"
ping -n 3 127.0.0.1 >nul
exit /b 0

:failed
echo.
echo [失败] 前端更新出错，请查看上方日志。
echo.
pause
exit /b 1
