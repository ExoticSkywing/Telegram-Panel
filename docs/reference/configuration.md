# 配置与数据目录

## 技术栈

- .NET 8 / ASP.NET Core Minimal API
- Vue 3 / Element Plus（主后台）
- Razor / MudBlazor（旧模块页面兼容）
- EF Core（默认 SQLite）
- WTelegramClient（MTProto）

## Docker 数据目录（强相关）

`docker-compose.yml` 会把宿主机 `./docker-data` 挂载到容器 `/data`，核心文件包括：

- `/data/telegram-panel.db`：SQLite 数据库
- `/data/sessions/`：账号 session 文件
- `/data/appsettings.local.json`：UI 保存后的本地覆盖配置
- `/data/admin_auth.json`：后台登录账号/密码（首次会用初始默认值生成）
- `/data/uploads/`：图片资产（数据字典图片、头像素材等）

## 后台任务（刷新页面不影响）

部分批量任务会在后台静默执行（避免“刷新页面就中断”）：

- 批量邀请
- 批量设置管理员

## 账号状态检测（深度探测）

为更可靠识别冻结/受限等状态，支持深度探测（例如通过创建/删除测试频道来探测权限）。

检测结果会持久化到数据库，避免刷新页面又变回“未检测”。

## 清理废号（封禁/受限/未登录/session 失效）

在「账号列表」与「外置验证码链接」页面支持“清理废号”（多选批量）：

- 会先执行 Telegram 状态检测（可选普通/深度）
- 仅当判定为废号（封禁/受限/被冻结/需要 2FA/Session 失效或损坏）才会删除
- 删除范围：数据库记录 + `*.session`（含常见备份/同名 json）
- 若遇到 `*.session` 文件被占用，会先尝试从 `TelegramClientPool` 释放客户端并重试删除

另外，系统「账号列表」支持“一键清理所有废号”（扫描系统全部账号）。

## 配置项速查

Docker 下常用环境变量（见 `docker-compose.yml`）：

- `ConnectionStrings__DefaultConnection`：SQLite 路径（默认 `/data/telegram-panel.db`）
- `Telegram__SessionsPath`：session 目录（默认 `/data/sessions`）
- `Telegram__Proxy__Server` / `Telegram__Proxy__Port`：Telegram 全局代理地址和端口
- `Telegram__Proxy__Username` / `Telegram__Proxy__Password`：SOCKS5 代理认证（可选）
- `Telegram__Proxy__Secret`：MTProxy Secret（可选；与用户名/密码二选一）
- `Proxy__Warp__Enabled`：允许面板管理独立 WARP 容器
- `Proxy__Warp__Network`：WARP 容器加入的 Docker 网络
- `Proxy__Warp__Protocol`：自动创建 WARP 时默认使用 `http` 或 `socks5`
- `Proxy__Warp__Maintenance__Enabled`：启用受管 WARP 出口巡检与故障恢复
- `Proxy__Warp__Maintenance__HealthCheckIntervalMinutes`：巡检周期，默认 5 分钟
- `Proxy__Warp__Maintenance__FailureThreshold`：连续失败恢复阈值，默认 2 次
- `Proxy__Warp__Maintenance__RecoveryCooldownMinutes`：失败恢复冷却，默认 30 分钟
- `Proxy__Warp__Maintenance__ScheduledRefreshEnabled`：是否定时重启健康出口，默认关闭
- `Proxy__Warp__Maintenance__ScheduledRefreshIntervalMinutes`：健康出口定时刷新周期，默认 720 分钟
- `AdminAuth__CredentialsPath`：后台密码文件（默认 `/data/admin_auth.json`）
- `Sync__AutoSyncEnabled`：账号创建的频道/群组自动同步（默认关闭）
- `Telegram__BotAutoSyncEnabled`：Bot 频道自动同步（默认关闭）
- `Telegram__WebhookEnabled`：Bot Webhook 模式开关（默认关闭，使用长轮询）
- `Telegram__WebhookBaseUrl`：Webhook 公网 HTTPS 地址
- `Telegram__WebhookSecretToken`：Webhook 验证密钥

## UI 保存到本地覆盖配置

面板里的部分“保存”按钮会把设置写入 `appsettings.local.json`（Docker 下为 `/data/appsettings.local.json`），常见键：

- `Telegram:BotAutoSyncEnabled` / `Telegram:BotAutoSyncIntervalSeconds`：Bot 频道后台自动同步轮询开关/间隔
- `ChannelAdminDefaults:Rights`：批量设置管理员的“默认权限”
- `ChannelAdminPresets:Presets`：批量设置管理员的“用户名列表预设”（名称 -> usernames）
- `ChannelInvitePresets:Presets`：批量邀请成员的“用户名列表预设”（名称 -> usernames）

## 账号代理优先于全局代理

代理管理中的 HTTP、SOCKS5、MTProxy、WARP 和 Resin 可以绑定到单个或多个账号。
账号的 Telegram 客户端、后台任务和模块操作都会复用这条账号路由。完整操作说明见
[代理管理与账号出口](../guides/proxy-management.md)。

路由优先级由账号明确选择决定：

- **已有代理**：使用账号绑定的代理。
- **全局设置**：继承下面的 `Telegram:Proxy`。
- **直连**：明确绕过账号代理和全局代理。

## 配置 Telegram 全局代理

在 `appsettings.local.json` 中配置后重启主程序，默认继承“全局设置”的账号会使用该代理：

```json
{
  "Telegram": {
    "Proxy": {
      "Server": "127.0.0.1",
      "Port": 40000,
      "Username": "",
      "Password": "",
      "Secret": ""
    }
  }
}
```

- 普通 SOCKS5 代理按需填写 `Username`、`Password`。
- MTProxy 填写 `Secret`，不需要用户名和密码。
- 账号管理中的“已有代理”优先于全局设置；“直连”会明确绕过全局代理；“全局设置”可恢复继承该配置。升级前已有账号默认继续继承全局设置。
- Docker 部署的配置文件位于宿主机 `docker-data/appsettings.local.json`。容器内的 `127.0.0.1` 指向容器自身；访问宿主机代理时应使用容器可访问的宿主机地址（Docker Desktop 通常可用 `host.docker.internal`），并确保代理监听地址和防火墙允许容器连接。
- 修改配置后必须重启主程序，以释放已缓存的 Telegram 客户端并按新代理重新连接。

## 配置受管 WARP 默认值

使用 `docker-compose.warp.yml` 时，在 `.env` 设置：

```dotenv
TP_WARP_DOCKER_NETWORK=telegram-panel_default
TP_WARP_PROXY_PROTOCOL=http
TP_WARP_AUTO_RECOVERY_ENABLED=true
TP_WARP_HEALTH_CHECK_INTERVAL_MINUTES=5
TP_WARP_FAILURE_THRESHOLD=2
TP_WARP_RECOVERY_COOLDOWN_MINUTES=30
TP_WARP_SCHEDULED_REFRESH_ENABLED=false
TP_WARP_SCHEDULED_REFRESH_INTERVAL_MINUTES=720
```

Compose 会映射为 `Proxy:Warp:Network` 和 `Proxy:Warp:Protocol`。修改后需要使用包含
`docker-compose.warp.yml` 的命令重新创建容器。代理管理中的一键创建弹窗可以覆盖单次
创建协议；导入、登录和批量绑定自动创建 WARP 时使用这里的默认值。

默认 `Proxy:Warp:ProxyHostMode=container` 不发布宿主端口。自定义为 `published` 时，
`Proxy:Warp:HostPortStart` 默认从 `42080` 起步；已占用或 Docker 绑定时发生冲突的端口会
自动跳过并递增重试，失败重建不会删除已经创建的 WARP 数据卷。

自动恢复会保留原数据卷，只重启容器并重新检测出口。健康出口的周期刷新默认关闭，
因为它可能改变账号公网 IP；需要与 tokens-pro 相同的 720 分钟刷新行为时再显式开启。

## Bot 启用/停用（每个 Bot）

机器人管理页可以对单个 Bot 启用/停用：停用后该 Bot 不会再被后台轮询 `getUpdates`，也不会被需要 Bot 的模块/任务使用。

## Bot Webhook 模式（生产环境推荐）

Bot Webhook 的完整配置与注意事项已单独整理：见 [Bot Webhook](../deployment/bot-webhook.md)。
