# ConvenientSystem 云服务器部署教程（Linux + SQLite）

> **适用场景**：从零开始将 ConvenientSystem 部署到阿里云轻量服务器（Ubuntu），使用 SQLite 替代 SQL Server。
>
> **前置条件**：本地开发环境已安装 .NET 10 SDK、Node.js 18+、SQL Server LocalDB。
>
> **最终效果**：浏览器访问 `http://公网IP` 打开系统，API + 前端 + 定时任务全部运行。

---

## 目录

1. [购买服务器](#1-购买服务器)
2. [本地数据迁移：SQL Server → SQLite](#2-本地数据迁移sql-server--sqlite)
3. [构建前端](#3-构建前端)
4. [编译 API（linux-x64）](#4-编译-apilinux-x64)
5. [准备部署文件](#5-准备部署文件)
6. [上传文件到服务器](#6-上传文件到服务器)
7. [服务器环境配置](#7-服务器环境配置)
8. [初始化数据库](#8-初始化数据库)
9. [配置 systemd 服务](#9-配置-systemd-服务)
10. [配置 Nginx 反向代理](#10-配置-nginx-反向代理)
11. [验证部署](#11-验证部署)
12. [后续更新流程](#12-后续更新流程只换-api-二进制)
13. [桌面客户端连接云服务器](#13-桌面客户端连接云服务器)
14. [常见问题](#14-常见问题)

---

## 1. 购买服务器

### 1.1 阿里云轻量应用服务器

1. 打开 [阿里云轻量应用服务器](https://www.aliyun.com/product/swas)
2. 选择套餐：**2核2G / 3M带宽 / 40GB ESSD**（最低配置，够用）
3. 地域：选离你用户最近的地域
4. 镜像：**Ubuntu 22.04 LTS**（或 24.04）
5. 购买时长：1年
6. 购买后进入控制台，记录 **公网 IP**

### 1.2 重置密码

1. 控制台 → 点击实例 → **重置密码**
2. 设置 root 密码（包含大小写字母+数字，如 `Cs@2026server`）
3. 重置后需要 **重启实例** 才生效

### 1.3 开放防火墙端口

控制台 → 实例详情 → **防火墙** → 添加规则：

| 协议 | 端口 | 源地址 | 备注 |
|------|------|--------|------|
| TCP | 22 | 0.0.0.0/0 | SSH 远程连接 |
| TCP | 80 | 0.0.0.0/0 | HTTP 网页访问 |

---

## 2. 本地数据迁移：SQL Server → SQLite

从本地 LocalDB 导出表结构和数据，生成 SQLite 兼容的 SQL 文件。

### 2.1 启动 LocalDB

打开 PowerShell：

```powershell
sqllocaldb start MSSQLLocalDB
```

### 2.2 执行迁移脚本

```powershell
cd e:\A-Chenzk\Code\MyProject\ConvenientSystem\bin
.\migrate-sqlite.ps1
```

脚本会读取 LocalDB 中所有用户表，转换类型并导出数据。

**输出文件**：`bin\migrate-sqlite.sql`（约 13MB，含 41 张表的建表语句 + 全量数据 INSERT）

### 2.3 补充时间字段默认值

迁移脚本生成的建表语句中，`CreateTime` / `CreatedAt` 等列是 `NOT NULL` 但没有 `DEFAULT` 值。而代码中有些字段标记了 `[Column(CanInsert = false)]`，FreeSql 不会在 INSERT 时提供值，SQLite 会报 `NOT NULL constraint failed`。

用 PowerShell 批量替换：

```powershell
$file = "e:\A-Chenzk\Code\MyProject\ConvenientSystem\bin\migrate-sqlite.sql"
$content = Get-Content $file -Raw
$content = $content -replace '"CreateTime" TEXT NOT NULL,', '"CreateTime" TEXT NOT NULL DEFAULT (datetime(''now'')),'
$content = $content -replace '"CreatedAt" TEXT NOT NULL,', '"CreatedAt" TEXT NOT NULL DEFAULT (datetime(''now'')),'
Set-Content $file -Value $content -Encoding UTF8
Write-Output "Done"
```

> **为什么需要这一步**：SQL Server 中这些列有 `GETDATE()` 默认值，SQLite 迁移脚本没有自动生成 DEFAULT 子句。

---

## 3. 构建前端

```powershell
cd e:\A-Chenzk\Code\MyProject\ConvenientSystem\web
npm run build
```

构建产物输出到 `ConvenientSystem.Desktop\wwwroot\`（Vite 配置的 `outDir`）。

---

## 4. 编译 API（linux-x64）

编译为 Linux 自包含单文件，不需要服务器安装 .NET 运行时。

```powershell
cd e:\A-Chenzk\Code\MyProject\ConvenientSystem
dotnet publish "ConvenientSystem.Api\ConvenientSystem.Api.csproj" `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:SkipVueBuild=true `
    -o "bin\server-deploy\api" `
    --nologo
```

**输出**：`bin\server-deploy\api\ConvenientSystem`（约 127MB，单个可执行文件）

---

## 5. 准备部署文件

### 5.1 创建服务器专用 appsettings.json

`dotnet publish` 会复制本地开发用的 `appsettings.json`（连 SQL Server），需要替换为 SQLite 配置。

编辑 `bin\server-deploy\api\appsettings.json`，内容改为：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Database": {
    "Type": "Sqlite"
  },
  "ConnectionStrings": {
    "ConvenientSystemDb": "Data Source=/app/data/convenient.db",
    "YhSystemDb": ""
  },
  "AppSettings": {
    "ServicePort": 51943
  },
  "AllowedHosts": "*"
}
```

**关键说明**：

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `Database:Type` | `Sqlite` | 切换到 SQLite 模式（本地开发用 `SqlServer`） |
| `ConvenientSystemDb` | `Data Source=/app/data/convenient.db` | 服务器上的 SQLite 文件路径 |
| `YhSystemDb` | `""`（空） | 云服务器访问不到内网数据库，留空会自动复用配置库 |
| `ServicePort` | `51943` | API 监听端口 |

### 5.2 复制前端文件

```powershell
# 创建前端部署目录
New-Item -ItemType Directory -Force -Path "bin\server-deploy\web"

# 复制前端构建产物
Copy-Item "ConvenientSystem.Desktop\wwwroot\*" "bin\server-deploy\web\" -Recurse -Force
```

### 5.3 复制迁移 SQL

```powershell
Copy-Item "bin\migrate-sqlite.sql" "bin\server-deploy\" -Force
```

### 5.4 创建部署目录结构确认

最终 `bin\server-deploy\` 目录结构应为：

```
bin\server-deploy\
├── api\
│   ├── ConvenientSystem              ← API 二进制（127MB）
│   ├── appsettings.json              ← 服务器专用 SQLite 配置
│   ├── appsettings.Development.json
│   ├── libe_sqlite3.so               ← SQLite 原生库
│   └── x64\
│       └── pdfium.dll                 ← PDF 渲染库
├── web\
│   ├── index.html
│   └── assets\                        ← 前端 JS/CSS
└── migrate-sqlite.sql                 ← 数据库迁移 SQL
```

---

## 6. 上传文件到服务器

### 6.1 安装 Posh-SSH（本地 PowerShell 模块）

```powershell
Install-Module Posh-SSH -Force -Scope CurrentUser
```

> 只需安装一次，以后更新代码上传也用这个。

### 6.2 打包部署文件

```powershell
$deployDir = "e:\A-Chenzk\Code\MyProject\ConvenientSystem\bin\server-deploy"
$tarFile = "e:\A-Chenzk\Code\MyProject\ConvenientSystem\bin\deploy-package.tar.gz"
tar -czf $tarFile -C $deployDir .
```

### 6.3 上传到服务器

```powershell
# 替换为你的服务器 IP 和密码
$ip = "123.56.68.132"
$password = "你的root密码"
$pass = ConvertTo-SecureString $password -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential("root", $pass)

# 上传 tar 包
Set-SCPItem -ComputerName $ip -Credential $cred -AcceptKey `
    -Path "e:\A-Chenzk\Code\MyProject\ConvenientSystem\bin\deploy-package.tar.gz" `
    -Destination "/root/"
```

上传完成后，文件在服务器的 `/root/deploy-package.tar.gz`。

---

## 7. 服务器环境配置

### 7.1 SSH 连接服务器

```powershell
$ip = "123.56.68.132"
$password = "你的root密码"
$pass = ConvertTo-SecureString $password -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential("root", $pass)
$session = New-SSHSession -ComputerName $ip -Credential $cred -AcceptKey
```

> 也可以用其他 SSH 工具（如 PuTTY、Xshell、阿里云 Workbench）连接，效果一样。以下命令在服务器终端执行。

### 7.2 安装依赖

```bash
apt-get update -qq
apt-get install -y nginx sqlite3
```

### 7.3 创建目录结构

```bash
mkdir -p /app/data
mkdir -p /app/api
mkdir -p /app/web
```

目录用途：

| 目录 | 用途 |
|------|------|
| `/app/api/` | API 二进制 + 配置文件 |
| `/app/web/` | 前端静态文件（Nginx 提供） |
| `/app/data/` | SQLite 数据库文件 |

### 7.4 解压部署文件

```bash
cd /root
mkdir -p deploy
tar -xzf deploy-package.tar.gz -C deploy
```

### 7.5 复制文件到目标目录

```bash
cp -rf /root/deploy/api/* /app/api/
cp -rf /root/deploy/web/* /app/web/
cp -f /root/deploy/migrate-sqlite.sql /app/
chmod +x /app/api/ConvenientSystem
```

### 7.6 配置 Swap（防止内存不足杀进程）

2GB 内存的服务器建议加 2GB swap 交换分区：

```bash
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

验证：

```bash
free -h
# 应看到 Swap: 2.0Gi 0B 2.0Gi
```

### 7.7 确保 SSH 开机自启

```bash
systemctl enable ssh
systemctl start ssh
```

### 7.8 配置防火墙

```bash
ufw allow 22/tcp
ufw allow 80/tcp
ufw --force enable
ufw status
```

---

## 8. 初始化数据库

### 8.1 从迁移 SQL 创建 SQLite 数据库

```bash
cd /app
sqlite3 /app/data/convenient.db < migrate-sqlite.sql
```

### 8.2 验证表创建成功

```bash
sqlite3 /app/data/convenient.db ".tables"
```

应输出所有表名（SysUser、SysMenu、SysConfig 等 40+ 张表）。

### 8.3 验证 AUTOINCREMENT

```bash
sqlite3 /app/data/convenient.db ".schema SysUser"
```

确认 Id 列是 `INTEGER PRIMARY KEY AUTOINCREMENT`，不是 `INTEGER NOT NULL`。

> 如果缺少 AUTOINCREMENT，说明迁移 SQL 有问题，回到第 2 步检查 `migrate-sqlite.ps1` 的输出。

---

## 9. 配置 systemd 服务

让 API 作为系统服务运行，开机自启、崩溃自动重启。

### 9.1 创建服务文件

```bash
cat > /etc/systemd/system/convenient-api.service << 'EOF'
[Unit]
Description=ConvenientSystem API Service
After=network.target

[Service]
Type=simple
WorkingDirectory=/app/api
ExecStart=/app/api/ConvenientSystem
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGES=false

[Install]
WantedBy=multi-user.target
EOF
```

### 9.2 启动服务

```bash
systemctl daemon-reload
systemctl enable convenient-api
systemctl start convenient-api
```

### 9.3 验证服务状态

```bash
systemctl is-active convenient-api
# 输出 active 表示运行中
```

### 9.4 查看启动日志

```bash
journalctl -u convenient-api -n 30 --no-pager
```

正常应看到 API 启动信息，没有 SQLite 报错。

---

## 10. 配置 Nginx 反向代理

Nginx 负责在 80 端口接收请求，转发到 API 的 51943 端口，同时提供前端静态文件。

### 10.1 创建 Nginx 配置

```bash
cat > /etc/nginx/sites-available/convenient << 'EOF'
server {
    listen 80;
    server_name _;

    # 前端静态文件
    root /app/web;
    index index.html;

    # API 反向代理
    location /api/ {
        proxy_pass http://127.0.0.1:51943;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Hangfire Dashboard 反向代理
    location /hangfire/ {
        proxy_pass http://127.0.0.1:51943;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # 前端 SPA 路由（所有非文件请求回退到 index.html）
    location / {
        try_files $uri $uri/ /index.html;
    }
}
EOF
```

### 10.2 启用配置

```bash
# 创建软链接启用站点
ln -sf /etc/nginx/sites-available/convenient /etc/nginx/sites-enabled/convenient

# 删除默认站点（避免冲突）
rm -f /etc/nginx/sites-enabled/default

# 测试配置语法
nginx -t
# 输出 "test is successful" 表示 OK

# 重启 Nginx
systemctl restart nginx
systemctl enable nginx
```

### 10.3 验证 Nginx 状态

```bash
systemctl is-active nginx
# 输出 active
```

---

## 11. 验证部署

### 11.1 检查端口监听

```bash
ss -tlnp | grep -E '80|51943'
```

应看到：
- Nginx 监听 80
- ConvenientSystem 监听 51943

### 11.2 本地测试 API

```bash
curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:51943/api/common/auth/captcha
# 输出 200 表示 API 正常
```

### 11.3 本地测试 Nginx

```bash
curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1
# 输出 200 表示 Nginx 正常
```

### 11.4 公网访问测试

在本地浏览器打开：

```
http://123.56.68.132
```

应看到登录页面。用 admin 账号登录，验证功能正常。

### 11.5 查看 API 日志

```bash
# 最近 50 条
journalctl -u convenient-api -n 50 --no-pager

# 实时跟踪
journalctl -u convenient-api -f
```

确认没有 `no such table` 或 `NOT NULL constraint failed` 等 SQLite 错误。

---

## 12. 后续更新流程（只换 API 二进制）

代码修改后，只需重新编译并替换二进制文件，无需重新部署整个环境。

### 12.1 本地编译

```powershell
cd e:\A-Chenzk\Code\MyProject\ConvenientSystem
dotnet publish "ConvenientSystem.Api\ConvenientSystem.Api.csproj" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:SkipVueBuild=true -o "bin\temp-publish" --nologo
```

### 12.2 上传新二进制

```powershell
$ip = "123.56.68.132"
$password = "你的root密码"
$pass = ConvertTo-SecureString $password -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential("root", $pass)

# 上传
Set-SCPItem -ComputerName $ip -Credential $cred -AcceptKey -Path "bin\temp-publish\ConvenientSystem" -Destination "/root/"
```

### 12.3 替换并重启

```powershell
$session = New-SSHSession -ComputerName $ip -Credential $cred -AcceptKey
Invoke-SSHCommand -SessionId $session.SessionId -Command "chmod +x /root/ConvenientSystem && systemctl stop convenient-api && cp /root/ConvenientSystem /app/api/ConvenientSystem && chmod +x /app/api/ConvenientSystem && systemctl start convenient-api && sleep 3 && systemctl is-active convenient-api" -TimeOut 60
Remove-SSHSession -SessionId $session.SessionId | Out-Null
```

> **注意**：只替换 `ConvenientSystem` 二进制文件，不要覆盖 `/app/api/appsettings.json`（那是服务器专用 SQLite 配置）。

### 12.4 更新前端（如有修改）

```powershell
# 上传前端文件
Set-SCPItem -ComputerName $ip -Credential $cred -AcceptKey -Path "ConvenientSystem.Desktop\wwwroot\*" -Destination "/app/web/" -ConnectionTimeout 30
```

---

## 13. 桌面客户端连接云服务器

修改本地 `ConvenientSystem.Desktop\appsettings.json`：

```json
{
  "AppSettings": {
    "RemoteServerUrl": "123.56.68.132:80"
  }
}
```

| 模式 | RemoteServerUrl | 说明 |
|------|-----------------|------|
| 本地模式 | `127.0.0.1:51943` | 桌面客户端自带 API（连内网 SQL Server） |
| 云端模式 | `123.56.68.132:80` | 连接云服务器 API（走 Nginx 80 端口） |

> 格式是 `IP:端口`，中间件会自动拼 `http://` 前缀。

修改后重启桌面客户端即可切换到云端模式。

---

## 14. 常见问题

### 14.1 `no such table: dbo.SysPublicPage`

**原因**：实体类的 `[Table(Name = "dbo.XXX")]` 带了 SQL Server 的 `dbo.` schema 前缀，SQLite 把 `dbo.SysPublicPage` 当成完整表名。

**解决**：去掉所有实体类的 `dbo.` 前缀，改为 `[Table(Name = "SysPublicPage")]`。SQL Server 默认 schema 就是 dbo，不带前缀也能找到。

### 14.2 `NOT NULL constraint failed: XXX.Id`

**原因**：SQLite 表的 Id 列缺少 `PRIMARY KEY AUTOINCREMENT`，FreeSql 标记了 `IsIdentity = true` 但表结构没有自增。

**解决**：检查 `migrate-sqlite.sql` 中 identity 列是否为 `INTEGER PRIMARY KEY AUTOINCREMENT`。如果缺少，重新生成迁移 SQL 或手动修复表结构。

### 14.3 `NOT NULL constraint failed: XXX.CreateTime`

**原因**：实体中 `[Column(CanInsert = false)]` 标记的字段，FreeSql 不会在 INSERT 中提供值。SQL Server 有 `GETDATE()` 默认值，SQLite 迁移时没有生成 DEFAULT。

**解决**：在建表 SQL 中给 `CreateTime` / `CreatedAt` 列加 `DEFAULT (datetime('now'))`。详见第 2.3 节。

### 14.4 SSH 连不上

**原因**：可能是 sshd 未启动、iptables 拦截、或安全组未放行 22 端口。

**排查步骤**：

1. 阿里云控制台 → 防火墙 → 确认 22 端口已放行
2. 阿里云控制台 → 远程连接 → 用 Workbench 或 VNC 进服务器
3. 检查 SSH 服务：`systemctl status ssh`
4. 如果未运行：`systemctl start ssh && systemctl enable ssh`
5. 检查 iptables：`iptables -L -n`，如果有 DROP 规则：`iptables -F && iptables -P INPUT ACCEPT`
6. 确认 SSH 在监听：`ss -tlnp | grep 22`

### 14.5 服务器内存不足导致进程被杀

**原因**：2GB 内存服务器在 .NET API + 彩票爬虫任务运行时可能内存紧张，Linux OOM Killer 会杀进程（包括 sshd）。

**解决**：

```bash
# 加 2GB swap
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# 验证
free -h
```

### 14.6 考勤界面报错

**原因**：考勤功能依赖内网 `192.168.16.7` 的 `yh_system` 数据库（`BuAttendance` + `DingtalkUser` 表），云服务器在公网访问不到。

**解决**：

- 在内网使用桌面客户端连本地 API，考勤正常
- 或将考勤数据也迁移到 SQLite（但需要定期同步）

### 14.7 Nginx 返回 502 Bad Gateway

**原因**：API 服务未运行或端口不对。

**排查**：

```bash
systemctl status convenient-api    # 检查 API 状态
ss -tlnp | grep 51943              # 检查端口监听
journalctl -u convenient-api -n 30 # 查看错误日志
```

### 14.8 数据库需要重置

如果数据库数据有问题需要重建：

```bash
systemctl stop convenient-api
rm /app/data/convenient.db
cd /app
sqlite3 /app/data/convenient.db < migrate-sqlite.sql
systemctl start convenient-api
```

> **警告**：这会删除所有数据，请确认后再操作。

---

## 附录：服务器目录结构总览

```
/app/
├── api/
│   ├── ConvenientSystem              ← API 可执行文件（127MB）
│   ├── appsettings.json              ← 服务器专用 SQLite 配置
│   ├── appsettings.Development.json
│   ├── libe_sqlite3.so               ← SQLite Linux 原生库
│   ├── ConvenientSystem.pdb          ← 调试符号（可选）
│   └── x64/
│       └── pdfium.dll                ← PDF 渲染库
├── web/
│   ├── index.html                    ← 前端入口
│   └── assets/                       ← JS/CSS 静态资源
├── data/
│   └── convenient.db                 ← SQLite 数据库文件
└── migrate-sqlite.sql                ← 迁移 SQL（留档备用）

/etc/systemd/system/
└── convenient-api.service            ← systemd 服务配置

/etc/nginx/sites-available/
└── convenient                        ← Nginx 站点配置
```

---

## 附录：核心命令速查

| 操作 | 命令 |
|------|------|
| 启动 API | `systemctl start convenient-api` |
| 停止 API | `systemctl stop convenient-api` |
| 重启 API | `systemctl restart convenient-api` |
| 查看状态 | `systemctl status convenient-api` |
| 查看日志 | `journalctl -u convenient-api -f` |
| 重启 Nginx | `systemctl restart nginx` |
| 查看内存 | `free -h` |
| 查看端口 | `ss -tlnp` |
| 查看数据库表 | `sqlite3 /app/data/convenient.db ".tables"` |
| 查看表结构 | `sqlite3 /app/data/convenient.db ".schema 表名"` |
| 执行 SQL | `sqlite3 /app/data/convenient.db` |
