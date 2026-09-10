@echo off
chcp 65001 >nul
title 构建桌面安装包 - ConvenientSystem
setlocal
set "ROOT=%~dp0"
cd /d "%ROOT%"
set "LOG=%ROOT%构建安装包.log"

echo ======================================== > "%LOG%"
echo ConvenientSystem 桌面安装包构建 >> "%LOG%"
echo %date% %time% >> "%LOG%"
echo ======================================== >> "%LOG%"

echo ========================================
echo   ConvenientSystem 桌面安装包构建
echo   1. 构建 Vue 前端
echo   2. 发布桌面客户端单文件 exe
echo   3. 生成 Inno Setup 安装包
echo.
echo 用法：构建安装包.cmd [版本号]
echo 未传版本号时自动将当前版本的末位加 1。
echo ========================================
echo.

echo [0/3] 更新应用版本号...
echo [0/3] 更新应用版本号... >> "%LOG%"
for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$v='%~1'; if(!$v){$m=[regex]::Match([IO.File]::ReadAllText('ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj'),'<Version>([\d\.]+)</Version>'); $p=$m.Groups[1].Value.Split('.'); $p[$p.Length-1]=[int]$p[$p.Length-1]+1; $v=$p -join '.'}; if($v -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){exit 1}; $e=New-Object Text.UTF8Encoding($false); $f='ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'<(Version|FileVersion|AssemblyVersion)>[\d\.]+</(Version|FileVersion|AssemblyVersion)>','<$1>'+$v+'</$1>'),$e); $q=[char]34; $f='ConvenientSystem.Desktop\appsettings.json'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'\x22DesktopVersion\x22\s*:\s*\x22[\d\.]+\x22',$q+'DesktopVersion'+$q+': '+$q+$v+$q),$e); $f='ConvenientSystem.Desktop\installer.iss'; [IO.File]::WriteAllText($f,[regex]::Replace([IO.File]::ReadAllText($f),'AppVersion=[\d\.]+','AppVersion='+$v),$e); Write-Output $v"`) do set "APPVER=%%V"
if not defined APPVER (
    echo [失败] 版本号应为 x.y.z 或 x.y.z.w。
    echo [失败] 版本号更新失败。 >> "%LOG%"
    goto :failed
)
echo   - 当前构建版本：%APPVER%
echo   - 当前构建版本：%APPVER% >> "%LOG%"
echo.

echo [1/3] 构建 Vue 前端...
echo [1/3] 构建 Vue 前端... >> "%LOG%"
cd web
set "NODE_OPTIONS=--max-old-space-size=4096"
if not exist "node_modules" (
    echo   - 正在安装前端依赖...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm ci 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
    if errorlevel 1 goto :failed
) else if not exist "node_modules\vite" (
    echo   - 正在修复前端依赖...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm install 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
    if errorlevel 1 goto :failed
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; npm run build 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
set "NODE_OPTIONS="
cd ..
echo   - Vue 前端构建成功
echo   - Vue 前端构建成功 >> "%LOG%"
echo.

echo [2/3] 发布桌面客户端...
echo [2/3] 发布桌面客户端... >> "%LOG%"
taskkill /F /IM ConvenientSystem.exe /T >> "%LOG%" 2>&1
ping -n 3 127.0.0.1 >nul
if exist "exe" rd /s /q "exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; dotnet publish 'ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SkipVueBuild=true -o 'exe' --nologo -v n 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed
echo   - 桌面客户端发布成功
echo   - 桌面客户端发布成功 >> "%LOG%"
echo.

echo [3/3] 生成 Inno Setup 安装包...
echo [3/3] 生成 Inno Setup 安装包... >> "%LOG%"
set "ISCC=iscc"
where iscc >nul 2>&1
if not errorlevel 1 goto :found_iscc
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
echo [失败] 未找到 Inno Setup。请安装：https://jrsoftware.org/isdl.php
echo [失败] 未找到 Inno Setup。 >> "%LOG%"
goto :failed

:found_iscc
echo   - 使用 ISCC：%ISCC%
echo   - 使用 ISCC：%ISCC% >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Continue'; & '%ISCC%' 'ConvenientSystem.Desktop\installer.iss' 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath '%LOG%' -Append; exit $LastExitCode"
if errorlevel 1 goto :failed

echo.
echo ========================================
echo   安装包构建完成！
echo   输出文件：installer-output\ConvenientSystem-Setup.exe
echo   版本号：%APPVER%
echo   构建日志：%LOG%
echo ========================================
echo.
start "" "%ROOT%installer-output\ConvenientSystem-Setup.exe"
ping -n 4 127.0.0.1 >nul
exit /b 0

:failed
echo.
echo [失败] 构建失败，请查看日志：%LOG%
echo.
pause
exit /b 1
