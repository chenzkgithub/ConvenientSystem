@echo off
chcp 65001 >nul
title 构建 Web 前端版本包 - ConvenientSystem
setlocal
cd /d "%~dp0"

echo ========================================
echo   ConvenientSystem Web 前端版本包构建
echo   将 Vue 构建产物打包为 zip，用于上传到服务器
echo ========================================
echo.

set "VERSION=%1"
if "%VERSION%"=="" (
    set /p "VERSION=请输入版本号（如 1.0.0）: "
)
if "%VERSION%"=="" (
    echo [错误] 版本号不能为空
    exit /b 1
)

echo [1/2] 构建 Vue 前端...
cd web
if not exist "node_modules" (
    echo   - 依赖缺失，执行 npm ci ...
    call npm ci
    if errorlevel 1 goto :failed
) else (
    if not exist "node_modules\vite" (
        echo   - 依赖不完整，执行 npm install 补全 ...
        call npm install
        if errorlevel 1 goto :failed
    )
)
call npm run build
if errorlevel 1 goto :failed
cd ..
echo.

echo [2/2] 打包 zip 版本包...
set "OUTPUT=web-package-%VERSION%.zip"
if exist "%OUTPUT%" del "%OUTPUT%"
powershell -Command "Compress-Archive -Path 'web\dist\*' -DestinationPath '%OUTPUT%' -Force"
if errorlevel 1 goto :failed
echo.

echo ========================================
echo   Web 前端版本包构建完成！
echo   版本号：%VERSION%
echo   输出文件：%~dp0%OUTPUT%
echo.
echo   下一步：在 Web 版本管理页面上传此 zip 文件
echo   管理页面：登录后菜单 系统管理 > Web版本管理
echo ========================================
echo.
exit /b 0

:failed
echo.
echo [失败] 构建出错，请查看上方日志！
timeout /t 3600 /nobreak >nul
