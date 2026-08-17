@echo off
chcp 65001 >nul
rem 窗口标题：任务栈与多窗口并存时能直接认出这个窗口是干什么的
title 开发环境 - 接口服务（端口 51943，关闭窗口即停止服务）
setlocal
rem 切换到脚本所在的目录
cd /d "%~dp0"

echo ========================================
echo   ConvenientSystem API 接口服务（开发模式）
echo   端口：http://0.0.0.0:51943
echo   关闭此窗口即停止服务
echo ========================================
echo.

rem 关闭之前运行的后端进程
echo 正在关闭旧进程...
taskkill /f /im ConvenientSystem.exe >nul 2>&1
taskkill /f /im dotnet.exe /fi "WINDOWTITLE eq *ConvenientSystem*" >nul 2>&1
timeout /t 2 /nobreak >nul
echo 旧进程已清理。
echo.

rem 使用 dotnet run 直接运行 API 项目（开发模式，自动编译）
dotnet run --project "ConvenientSystem.Api\ConvenientSystem.Api.csproj" -c Debug

rem 若程序退出或编译失败，报错后保持窗口不关闭，便于查看日志
rem 不用 pause，避免按键后无响应；用 timeout /nobreak 保持窗口，需要时手动关闭。
:hold
timeout /t 3600 /nobreak >nul
goto :hold
