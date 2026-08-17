@echo off
chcp 65001 >nul
rem 窗口标题：任务栈与多窗口并存时能直接认出这个窗口是干什么的
title 启动器 - 接口服务与桌面客户端（发布版）
setlocal EnableExtensions
cd /d "%~dp0"

echo ========================================
echo   ConvenientSystem 启动器
echo   1) 接口服务    api\ConvenientSystem.exe   端口 51943
echo   2) 桌面客户端  exe\ConvenientSystem.exe   端口 51942
echo ========================================
echo.
echo 说明：桌面客户端只提供前端静态页面，通过 /api 反向代理访问接口服务 51943。
echo       若本机未运行接口服务，客户端虽能打开界面但无法加载菜单数据。
echo.

if not exist "api\ConvenientSystem.exe" (
    echo [失败] 未找到 api\ConvenientSystem.exe
    echo        请先运行 publish.cmd 完成发布
    goto :hold
)
if not exist "exe\ConvenientSystem.exe" (
    echo [失败] 未找到 exe\ConvenientSystem.exe
    echo        请先运行 publish.cmd 完成发布
    goto :hold
)

rem ========== 1/2 启动接口服务 ==========
rem 接口服务是控制台窗口，保持该窗口存在，便于查看日志；关闭该窗口即停止接口服务。
call :islistening 51943
if "%_listening%"=="1" (
    echo [1/2] 接口服务已运行（51943），跳过启动
) else (
    echo [1/2] 启动接口服务 ...
    start "接口服务 - 端口 51943，关闭本窗口即停止服务" /d "%~dp0api" "%~dp0api\ConvenientSystem.exe"
)

rem ========== 等待接口服务就绪（最多 30 秒） ==========
echo       等待接口服务就绪 ...
set /a _waited=0
:waitapi
call :islistening 51943
if "%_listening%"=="1" goto :apiready
set /a _waited+=1
if %_waited% GEQ 30 (
    echo.
    echo [警告] 已等待 30 秒，端口 51943 仍未监听，接口服务启动可能失败。
    echo        客户端随后会报错，可先查看接口服务窗口的启动日志。
    goto :apiready
)
timeout /t 1 /nobreak >nul
goto :waitapi

:apiready
echo       接口服务已就绪
echo.

rem ========== 2/2 启动桌面客户端 ==========
echo [2/2] 启动桌面客户端 ...
start "" /d "%~dp0exe" "%~dp0exe\ConvenientSystem.exe"

echo.
echo ========================================
echo   启动完成。
echo   停止接口服务：关闭"接口服务"那个窗口。
echo ========================================
echo.
exit /b 0

rem ----------------------------------------
rem 子过程：判断指定端口是否处于 LISTENING 状态，结果写入 _listening=1 / 0。
rem 只匹配 LISTENING 行中的端口（LISTENING 行的远端地址为 0.0.0.0:0），避免误配。
rem ----------------------------------------
:islistening
set "_listening=0"
netstat -ano | findstr "LISTENING" | findstr ":%~1" >nul 2>nul
if not errorlevel 1 set "_listening=1"
goto :eof

rem 失败时保持窗口不关闭，便于查看错误信息。
:hold
timeout /t 3600 /nobreak >nul
goto :hold
