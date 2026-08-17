@echo off
chcp 65001 >nul
rem 窗口标题：任务栈与多窗口并存时能直接认出这个窗口是干什么的
title 发布打包 - 接口服务与桌面客户端（Release win-x64 单文件）
setlocal
rem 切换到脚本所在的目录（%~dp0 为当前脚本所在的目录路径）
cd /d "%~dp0"

echo ========================================
echo   ConvenientSystem 发布
echo   目标：Release / win-x64 / 单文件 / 自包含
echo   输出到：api\（接口服务） + exe\（桌面客户端）
echo ========================================
echo.

echo [1/5] 关闭正在运行的程序...
taskkill /f /im ConvenientSystem.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo.

echo [2/5] 清理旧的发布和构建产物...
if exist "api" rd /s /q "api"
if exist "exe" rd /s /q "exe"
rem 清理各 obj/bin，StaticWebAssets 清单会缓存旧前端 hash 文件（如 wwwroot\assets\*-xxxx.js），
rem 前端修改后构建的文件名变化，旧清单会导致 "No file exists for the asset ..." 启动失败。
if exist "ConvenientSystem.Shared\obj" rd /s /q "ConvenientSystem.Shared\obj"
if exist "ConvenientSystem.Shared\bin" rd /s /q "ConvenientSystem.Shared\bin"
if exist "ConvenientSystem.Service\obj" rd /s /q "ConvenientSystem.Service\obj"
if exist "ConvenientSystem.Service\bin" rd /s /q "ConvenientSystem.Service\bin"
if exist "ConvenientSystem.Api\obj" rd /s /q "ConvenientSystem.Api\obj"
if exist "ConvenientSystem.Api\bin" rd /s /q "ConvenientSystem.Api\bin"
if exist "ConvenientSystem.Desktop\obj" rd /s /q "ConvenientSystem.Desktop\obj"
if exist "ConvenientSystem.Desktop\bin" rd /s /q "ConvenientSystem.Desktop\bin"
rem 清理旧的前端构建产物（Vite 输出的 Desktop wwwroot，只保留 hash 文件，避免与发布冲突）
if exist "ConvenientSystem.Desktop\wwwroot\assets" rd /s /q "ConvenientSystem.Desktop\wwwroot\assets"
echo.

echo [3/5] 构建 Vue 前端...
cd web
rem 若依赖缺失，需要重新安装；npm ci 更干净，
rem   但 npm ci 会先删除 node_modules 再安装；若上一次 vite dev（dev-web.cmd）正在运行，
rem   占用住 node_modules\@esbuild\win32-x64\esbuild.exe，删除会报 EPERM 失败。
rem   而此时 node_modules 已被删了，vite 会丢失，导致后续 npm run build 报 "'vite' 不是内部或外部命令"。
rem   因此，仅在全缺失时使用 npm ci，部分缺失时 npm install 原地补全（不删除已锁定文件，避免占用影响）。
if not exist "node_modules" (
    echo   - 依赖缺失，执行 npm ci ...
    call npm ci
    if errorlevel 1 goto :failed
) else (
    if not exist "node_modules\vite" (
        echo   - 依赖不完整，执行 npm install 补全 ...
        call npm install
        if errorlevel 1 goto :failed
    ) else (
        echo   - 依赖已就绪，跳过安装
    )
)
call npm run build
if errorlevel 1 goto :failed
cd ..
rem npm 运行期间会把控制台标题改成 "npm run build"，此处恢复为本脚本的标题
title 发布打包 - 接口服务与桌面客户端（Release win-x64 单文件）
echo.

echo [4/5] 发布桌面客户端到 exe\...
dotnet publish "ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SkipVueBuild=true -o "exe" --nologo
if errorlevel 1 goto :failed
echo.

echo [5/5] 发布接口服务到 api\...
dotnet publish "ConvenientSystem.Api\ConvenientSystem.Api.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SkipVueBuild=true -o "api" --nologo
if errorlevel 1 goto :failed
echo.

rem ========================================
rem 配置文件说明（如需调整，可编辑 appsettings.json）：
rem   发布输出目录的 appsettings.json 由 dotnet publish 从各目标项目直接复制，内容保持不变。
rem     ConvenientSystem.Api\appsettings.json     -> api\appsettings.json
rem       ServicePort 配置接口服务端口（默认 51943）
rem     ConvenientSystem.Desktop\appsettings.json -> exe\appsettings.json
rem       RemoteServerUrl 指向接口服务地址（格式 IP:端口，默认 127.0.0.1:51943）
rem ========================================

echo.
echo ========================================
echo   发布完成！
echo   启动方式：运行 %~dp0start.cmd  同时启动接口服务和桌面客户端
echo   接口服务：%~dp0api\ConvenientSystem.exe   端口 51943
echo   桌面客户端：%~dp0exe\ConvenientSystem.exe   端口 51942（/api 代理到 51943）
echo   注意：请先双击 exe 之前，先启动接口服务，否则打开后菜单等接口会全失败。
echo ========================================
echo.
exit /b 0

:failed
echo.
echo [失败] 发布出错，请查看上方日志！
echo.
goto :hold

rem 失败时保持窗口不关闭，便于查看和复制错误信息。
:hold
timeout /t 3600 /nobreak >nul
goto :hold
