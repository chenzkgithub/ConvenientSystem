@echo off
chcp 65001 >nul
rem 窗口标题：任务栈与多窗口并存时能直接认出这个窗口是干什么的
title 开发环境 - 前端热更新服务（端口 5173，关闭窗口即停止）
setlocal
rem 切换到脚本所在的目录
cd /d "%~dp0"

echo ========================================
echo   前端开发服务器（Vite HMR 热更新）
echo   访问地址：http://localhost:5173
echo   接口代理到：http://localhost:51943
echo ========================================
echo.
echo 提示：请先运行 dev-backend.cmd 启动接口服务（端口 51943），否则接口调用会失败。
echo.

cd web
rem 不走 npm run dev：npm 启动时会把 process.title 设为 "npm run dev"，
rem 而 Windows 上 Node 的 process.title 直接写入控制台标题，会把上面设的窗口标题顶掉。
rem 下面一行与 web/package.json 的 "dev" 脚本完全等价，修改那个脚本时需同步本行。
call node -r ./polyfill.cjs ./node_modules/vite/bin/vite.js

rem 若程序退出或编译失败，报错后保持窗口不关闭，便于查看日志
rem 不用 pause，避免按键后无响应；用 timeout /nobreak 保持窗口，需要时手动关闭。
:hold
timeout /t 3600 /nobreak >nul
goto :hold
