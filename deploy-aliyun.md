# 阿里云轻量服务器部署指南（¥198/年方案）

针对 **ConvenientSystem** 项目的最便宜正式部署方案：
- 阿里云轻量应用服务器 2核4G / 4M 带宽 / 60G SSD
- Windows Server 2022
- SQL Server Express（免费版）
- IIS 反向代理 + HTTPS

预计总费用：**¥198/年（新用户首年）+ 域名费用（可选）**

---

## 一、购买服务器

1. 进入 [阿里云轻量应用服务器](https://www.aliyun.com/product/swas)
2. 选择套餐：**2核4G，4M 带宽，60G SSD**
3. 地域：选择离你用户最近的地域（华东 1 杭州 / 华东 2 上海 / 华南 1 深圳）
4. 镜像：选择 **Windows Server 2022 中文版**
5. 购买时长：1 年（新用户首年 ¥198）
6. 购买后进入控制台 → 轻量应用服务器 → 重置密码（首次连接需要）
7. 记录公网 IP

> 注意：第二年续费会恢复原价（约 ¥600～1000/年），到期前可关注阿里云续费活动或迁移到 ECS。

---

## 二、远程连接服务器

1. 在阿里云控制台点击「远程连接」
2. 使用 Windows 远程桌面（RDP）连接：
   - 计算机：`你的公网IP`
   - 用户名：`Administrator`
   - 密码：刚才重置的密码

---

## 三、安装基础环境

### 3.1 安装 SQL Server Express

1. 下载 SQL Server 2022 Express：
   ```
   https://go.microsoft.com/fwlink/?linkid=2216019
   ```
2. 运行安装程序，选择「基本」安装
3. 安装完成后，下载并安装 **SQL Server Management Studio (SSMS)**：
   ```
   https://aka.ms/ssmsfullsetup
   ```
4. 打开 SSMS，连接 `localhost\SQLEXPRESS`
5. 启用 SQL Server 身份验证：
   - 右键服务器 → 属性 → 安全性 → SQL Server 和 Windows 身份验证模式
   - 重启 SQL Server 服务
6. 创建登录名 `cs_user`，设置强密码，赋予 `dbcreator` + `sysadmin` 角色

### 3.2 安装 .NET 10 Runtime

1. 下载并安装 **ASP.NET Core 10.0 Runtime Hosting Bundle**：
   ```
   https://dotnet.microsoft.com/download/dotnet/10.0
   ```
2. 安装后重启服务器（或至少重启 IIS）

### 3.3 安装 IIS

1. 打开「服务器管理器」→「管理」→「添加角色和功能」
2. 一路下一步到「服务器角色」
3. 勾选 **Web 服务器 (IIS)**
4. 在「角色服务」中确保勾选：
   - 常见 HTTP 功能 → 静态内容
   - 应用程序开发 → ASP.NET 4.8、ISAPI 扩展、ISAPI 筛选器
   - 安全性 → 请求筛选
5. 完成安装

---

## 四、配置防火墙和安全组

### 4.1 阿里云安全组

在轻量服务器控制台 → 安全 → 防火墙，放行：

| 端口 | 说明 |
|---|---|
| 80 | HTTP |
| 443 | HTTPS |
| 3389 | 远程桌面（可限制仅自己 IP） |

**不要放行 51943**，API 只通过 IIS 反向代理暴露。

### 4.2 Windows 防火墙

服务器内 Windows 防火墙通常默认放行，如未放行：
```powershell
New-NetFirewallRule -DisplayName "HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
New-NetFirewallRule -DisplayName "HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

---

## 五、准备数据库

### 5.1 创建 ConvenientSystemDb

在 SSMS 中执行：
```sql
CREATE DATABASE ConvenientSystem;
GO
```

### 5.2 迁移本地数据（可选）

如果你本地已有数据：
1. 在本地 SQL Server / LocalDB 中备份：
   ```sql
   BACKUP DATABASE ConvenientSystem TO DISK = 'C:\Temp\ConvenientSystem.bak';
   ```
2. 把 `.bak` 文件复制到服务器
3. 在服务器 SSMS 中还原：
   ```sql
   RESTORE DATABASE ConvenientSystem FROM DISK = 'C:\Temp\ConvenientSystem.bak' WITH REPLACE;
   ```

### 5.3 YhSystemDb 处理

如果你的 `YhSystemDb` 是内网 `192.168.16.7` 那台服务器：
- 方案 A：那台服务器有公网 IP → 直接连公网 IP
- 方案 B：没有公网 IP → 把 `yh_system` 数据库也迁到这台轻量服务器
- 方案 C：服务器在内网但你想安全访问 → 用阿里云 VPN/专线（贵，不推荐）

---

## 六、发布 API

### 6.1 本地发布

在开发电脑 PowerShell 中执行：
```powershell
cd E:\A-Chenzk\MyProject\ConvenientSystem
dotnet publish ConvenientSystem.Api\ConvenientSystem.Api.csproj -c Release -o .\publish\api
```

### 6.2 上传到服务器

把 `publish\api` 文件夹压缩，通过远程桌面复制到服务器：
```
C:\www\convenientsystem\api\
```

### 6.3 修改生产配置

编辑服务器上的 `C:\www\convenientsystem\api\appsettings.json`：

```json
{
  "ConnectionStrings": {
    "ConvenientSystemDb": "server=localhost\\SQLEXPRESS;user id=cs_user;password=你的强密码;database=ConvenientSystem;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;pooling=true;min pool size=5;max pool size=512;Connection Timeout=30;",
    "YhSystemDb": "server=你的YhSystemDb地址;user id=sa;password=密码;database=yh_system;..."
  },
  "AppSettings": {
    "LoginAccount": "admin",
    "LoginPassword": "修改默认密码",
    "EnableLock": false,
    "ServicePort": 51943,
    "AuditLogRetentionDays": 60,
    "PublicAppUrl": "https://你的域名或IP"
  },
  "Jwt": {
    "Key": "这里换成至少32位的随机字符串"
  },
  "AllowedHosts": "你的域名"
}
```

> 如果暂时没有域名，`PublicAppUrl` 可以先用 `http://你的公网IP`，但企业微信卡片最好尽快换域名+HTTPS。

---

## 七、IIS 部署 API

### 7.1 创建应用程序池

1. 打开 IIS 管理器
2. 应用程序池 → 添加应用程序池
3. 名称：`ConvenientSystemApi`
4. .NET CLR 版本：**无托管代码**
5. 托管管道模式：**集成**
6. 启动 32 位应用程序：**False**
7. 标识：改为 `NetworkService` 或自定义有文件夹权限的账户

### 7.2 创建站点

1. 网站 → 添加网站
2. 网站名称：`ConvenientSystem`
3. 物理路径：`C:\www\convenientsystem\api`
4. 应用程序池：选择 `ConvenientSystemApi`
5. 端口：80（先 HTTP，后面再配 HTTPS）
6. 主机名：留空（用 IP 访问）或填域名

### 7.3 设置文件夹权限

给 `C:\www\convenientsystem\api` 文件夹添加 `IIS_IUSRS` 和 `NETWORK SERVICE` 的读取/执行权限。

### 7.4 测试 API

浏览器访问：
```
http://你的公网IP/api/Common/LotteryResult/GetSummary
```
应返回 JSON 数据。

---

## 八、配置域名和 HTTPS（强烈建议）

企业微信对外跳转链接用 **备案域名 + HTTPS**，否则容易被微信拦截。

### 8.1 购买域名

1. 阿里云域名注册，`.com` 约 ¥60/年，`.cn` 约 ¥30/年
2. 实名认证 + ICP 备案（约 7～20 天）
3. 解析域名到服务器公网 IP

### 8.2 申请 SSL 证书

1. 阿里云 SSL 证书 → 免费证书 → 申请 DV 证书
2. 按指引验证域名所有权
3. 下载 **IIS 格式** 证书（.pfx 文件）

### 8.3 IIS 绑定 HTTPS

1. IIS → 站点 → 绑定 → 添加
2. 类型：https
3. 端口：443
4. 主机名：你的域名
5. SSL 证书：选择导入的证书
6. 安装 **URL 重写模块**（IIS 扩展）：
   ```
   https://www.iis.net/downloads/microsoft/url-rewrite
   ```
7. 添加规则：HTTP 自动跳转 HTTPS

### 8.4 更新 PublicAppUrl

```json
"PublicAppUrl": "https://你的域名"
```

---

## 九、配置桌面客户端

修改每个用户电脑上 `ConvenientSystem.Desktop\appsettings.json`：

```json
{
  "AppSettings": {
    "RemoteServerUrl": "你的域名:443"
  }
}
```

> 格式必须是 `IP:端口` 或 `域名:端口`，中间件会自动拼 `http://`。因为走 HTTPS，端口写 443。

重启桌面客户端后，它会从本机 API 模式切换到远程云 API。

---

## 十、配置企业微信群机器人

1. 进入系统 → 群机器人配置
2. Webhook URL 填企业微信机器人地址
3. 类型选「企业微信」
4. 消息类型选「富文本卡片」
5. 触发一次开奖通知任务测试
6. 收到的卡片点击后应跳转到：
   ```
   https://你的域名/#/lottery-result-summary?standalone=1&date=2026-08-12
   ```

---

## 十一、设置自动启动

### 11.1 使用 IIS 托管（推荐）

IIS 会自动在系统启动时启动网站和应用程序池，无需额外配置。

### 11.2 备份策略

1. 数据库每日自动备份
2. 在 SQL Server Agent 中创建作业：
   ```sql
   BACKUP DATABASE ConvenientSystem TO DISK = 'C:\Backup\ConvenientSystem_$(DATE).bak';
   ```
3. 使用阿里云 OSS 客户端或脚本，把 `C:\Backup` 每天同步到 OSS

### 11.3 日志清理

在 `appsettings.json` 中配置日志保留，避免磁盘占满：
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```

---

## 十二、性能优化建议

2核4G 同时跑 API + SQL Server Express 比较紧张，建议：

1. SQL Server Express 内存限制为 1GB，如果数据量大需升级到 Web/Standard 版
2. 定期清理 `EmailLog`、`SysWebhookLog`、`AuditLog` 等日志表
3. 如果用户超过 10 人，考虑升级到 ECS 4核8G 或单独 RDS

---

## 十三、常见问题

**Q：浏览器访问 API 返回 502.5？**
A：.NET Runtime 没装好，或应用程序池 .NET CLR 版本选成了 v4.0。应选「无托管代码」。

**Q：SQL Server 连不上？**
A：检查 SQL Server 服务是否启动、TCP/IP 是否启用、防火墙 1433 端口是否放行。

**Q：企业微信卡片点击打不开？**
A：检查 `PublicAppUrl` 是否能从公网访问；必须使用备案域名 + HTTPS。

**Q：第二年续费太贵怎么办？**
A：到期前迁移到普通 ECS 新购套餐，或重新注册阿里云账号买新用户套餐（需迁移数据）。

---

## 十四、总费用清单

| 项目 | 首年费用 | 续费费用 |
|---|---|---|
| 轻量服务器 2核4G | ¥198 | ¥600～1000/年 |
| 域名 `.com` | ¥60 | ¥60～80/年 |
| SSL 证书 | ¥0 | ¥0 |
| SQL Server Express | ¥0 | ¥0 |
| 合计 | **约 ¥258** | 约 ¥700～1100/年 |

这是能正式跑起来的最低成本方案。后续用户多了再升级到 ECS + RDS。
