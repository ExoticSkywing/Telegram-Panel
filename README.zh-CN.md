# Telegram Panel

[English](README.md) | [中文](README.zh-CN.md)

基于 **WTelegramClient** 的 Telegram 多账户管理面板，使用 **.NET 8 后端** 与 **Vue 3 管理后台** 构建。

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0">
  <img src="https://img.shields.io/badge/Vue-3-42B883?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue 3">
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker Compose">
  <img src="https://img.shields.io/badge/Powered%20by-WTelegramClient-333333?style=for-the-badge" alt="Powered by WTelegramClient">
</p>

<p align="center">
  <a href="https://t.me/zhanzhangck"><img src="https://img.shields.io/badge/Telegram-站长仓库-blue?logo=telegram" alt="Telegram 站长仓库"></a>
  <a href="https://t.me/vpsbbq"><img src="https://img.shields.io/badge/Telegram-NexHub_AI社区-blue?logo=telegram" alt="Telegram NexHub AI社区"></a>
</p>

<p align="center">
  📚 <b><a href="https://moeacgx.github.io/Telegram-Panel/">文档站</a></b> |
  🖼️ <b><a href="screenshot/">截图</a></b>
</p>

## 项目简介

Telegram Panel 用于在单个 Web 面板中统一管理和运营多个 Telegram 账号，重点覆盖账号生命周期管理、批量运营、频道/群组管理、自动化任务以及模块扩展能力。

## 当前功能

- 📥 **账号接入**：支持 Telethon / TData 压缩包和 StringSession 导入，Telethon / TData 导出，手机号验证码登录、二维码登录与 2FA 验证
- 🌐 **账号级代理**：统一管理 HTTP、SOCKS5、MTProxy 与 Resin，支持出口检测、单账号 / 批量绑定，并在导入或登录前确定首次连接出口
- ☁️ **独立 WARP**：Linux Docker 环境可一键创建独立 Cloudflare WARP 容器；支持 HTTP / SOCKS5，并可为批量导入的账号自动创建和绑定
- 🛡️ **账号维护**：支持状态检测、废号清理、踢出其他设备、二级密码与找回邮箱管理，以及登录时长风险提示
- 👥 **频道与群组**：支持创建、同步、分类、筛选、邀请、管理员设置、公开化、退出、解散和链接导出
- 🤖 **Bot 与批量运营**：支持 Bot 管理、频道关联、批量加群 / 订阅 / 退群 / 启动 Bot，以及持续活跃等后台任务
- 🕒 **任务与数据字典**：支持立即 / 定时任务、暂停、编辑、重跑、自动清理，以及文本 / 图片字典和模板变量
- 🧠 **AI 辅助**：持续活跃任务可使用 OpenAI 兼容接口处理验证按钮或文本问题，并支持模型预设、重试和连通测试
- 🧩 **模块与外部 API**：支持安装 `.tpm` / `.zip` 模块包，扩展任务、API 和管理页面，并保留旧 Razor 模块页面兼容入口

## 代理由账号统一管理

导入、手机号登录和二维码登录必须在第一条 Telegram 请求前选择已有代理、独立 WARP、
有效的全局代理，或明确确认风险后直连。所选出口会在整个授权会话中冻结；验证失败时
不会静默回退到面板公网 IP。账号切换代理时，宿主会先停用账号并断开旧客户端，再用
新出口重建连接。

模块不需要再提供一套 Telegram 代理设置。模块应通过宿主已注册的账号服务复用账号客户端，
账号已经绑定的代理会由宿主自动应用。模块不要自行创建绕过宿主连接池的 Telegram 客户端，
否则可能造成同一账号从不同出口同时连接。只有模块访问与 Telegram 无关的第三方服务时，
才应按该服务的实际需要单独设计网络配置。

代理类型、首次连接规则、WARP 和 Resin 配置见
[代理管理与账号出口](docs/guides/proxy-management.md)。

## 前端与模块边界

主后台是入口位于 `/ui` 的 Vue SPA；`.NET` 后端负责 API、后台任务、账号连接、模块加载与
兼容路由。开发模块时遵循以下边界：

- 新模块优先提供 `/api/panel/extensions/{module-slug}` 管理接口，由 Vue 页面消费数据。
- 旧模块仍可通过 `IModuleUiProvider.GetPages` 注册 Razor 页面，并从 `/ext/{moduleId}/{pageKey}` 加载。
- 宿主已有 Vue 原生页面的模块必须提供对应管理接口，否则页面会返回 `404` 或回退到兼容页。
- 公开分享页通过 `MapEndpoints` 单独暴露，并自行处理令牌过期、权限隔离、防缓存和限流。

完整约定见[模块开发文档](docs/developer/modules.md)。

## 快速开始

### 获取稳定版本

- Docker：`ghcr.io/moeacgx/telegram-panel:latest`（Linux amd64 / arm64）
- Windows：从 [GitHub Releases](https://github.com/moeacgx/Telegram-Panel/releases/latest) 下载最新的 `windows-x64-setup.exe`
- Linux 独立包：从 [GitHub Releases](https://github.com/moeacgx/Telegram-Panel/releases/latest) 下载对应架构的 ZIP

### Docker 一键部署（推荐）

环境要求：Docker（Windows 推荐 Docker Desktop + WSL2；Linux 直接安装 Docker Engine）

#### 第一步：准备项目

```bash
git clone https://github.com/moeacgx/Telegram-Panel
cd Telegram-Panel
cp .env.example .env
```

#### 第二步：选择镜像版本

默认使用稳定版，无需改动：

```bash
TP_IMAGE=ghcr.io/moeacgx/telegram-panel:latest
```

如果需要开发版，可将 `.env` 改为：

```bash
TP_IMAGE=ghcr.io/moeacgx/telegram-panel:dev-latest
```

#### 第三步：启动服务

```bash
docker compose pull
docker compose up -d
```

访问：`http://localhost:5000`

如果宿主机 `5000` 端口被其它服务占用，建议只改宿主机端口，容器内仍保持 `5000`：

```yaml
ports:
  - "18080:5000"
```

然后访问：`http://localhost:18080`

#### 账号首连出口安全规则

导入账号、手机号登录和二维码登录都必须在第一条 Telegram 请求发出前明确选择出口：

- 已启用的现有代理
- 为本账号一键创建的独立 WARP
- 已正确配置的全局 Telegram 代理
- 用户明确确认风险后的直连

未选择出口、所选代理失效或全局代理配置无效时，操作会在连接 Telegram 之前直接失败，
不会静默回退到面板公网 IP。所选出口会在整个授权会话内冻结，发送或重发验证码、
二维码轮询和二级密码验证都会继续使用同一路由。

登录或导入成功后，账号会先以停用状态入库；只有同一路由绑定完成，且 Resin 临时租约
成功继承到正式账号身份后，账号才会启用。账号切换代理时也会先停用并严格断开旧客户端，
确认旧出口已停止后才提交新路由。任一步骤失败时账号保持停用，避免出现“先直连、后补代理”
或内存客户端仍沿用旧 IP 的情况。

#### 启用 WARP 一键创建（可选）

普通代理与 Resin 不需要 Docker Socket。只有需要面板创建独立 WARP 容器时，才显式叠加受管 WARP 配置：

```bash
docker compose -f docker-compose.yml -f docker-compose.warp.yml up -d
```

该配置会把 `/var/run/docker.sock` 挂入面板容器，其权限接近宿主机 `root`，只应在可信主机启用。默认网络名为 `telegram-panel_default`；若 Compose 项目名不同，可在 `.env` 设置：

```dotenv
TP_WARP_DOCKER_NETWORK=实际的_Docker_网络名
TP_WARP_PROXY_PROTOCOL=socks5
```

`TP_WARP_PROXY_PROTOCOL` 用于导入、登录和批量绑定自动创建 WARP 时的默认协议，可选
`http` 或 `socks5`，未设置时默认 `http`。WARP 镜像的 GOST 监听端口同时支持
HTTP 与 SOCKS5；代理管理中的一键创建弹窗还可以只覆盖本次创建的协议。

默认 `container` 模式不会占用宿主机端口。若自定义为 `published` 模式，面板会从
`Proxy:Warp:HostPortStart`（默认 `42080`）开始向后寻找可用端口；即使端口在 Docker
启动前一瞬间被其他进程抢占，也会清理失败容器、保留 WARP 数据卷，并自动改用下一端口。

受管 WARP 默认每 5 分钟自动检测出口，连续失败 2 次后重启容器并复测；恢复前后会
释放绑定账号的客户端，让它们仍通过原 WARP 路由重连，绝不回退面板直连。健康出口
默认不会定时重启，避免无故更换 Telegram 账号 IP；如需参考 tokens-pro 的 720 分钟
定时刷新，可在 `.env` 设置 `TP_WARP_SCHEDULED_REFRESH_ENABLED=true`。账号导入、
手机号登录或二维码登录正在使用某个 WARP 时，巡检、手动刷新、修改和删除都会避让，
避免首次连接中途更换出口。

代理管理页顶部检测的是**面板服务自身出口**，代理列表每一行检测的是对应代理的独立出口。
因此顶部显示“未使用 WARP”并不代表列表中的独立 WARP 代理不可用。

生产环境建议通过 `Proxy__Warp__Image` 固定经过审计的镜像 digest，而不是长期跟随可变的 `latest` 标签。

#### 对接 Resin（可选）

先按 [Resin 中文文档](https://github.com/Resinat/Resin/blob/master/README.zh-CN.md)
独立部署网关，然后在“代理管理”中新建 `Resin` 类型代理：

- 主机 / 端口：Resin HTTP 或 SOCKS5 数据面地址，默认端口通常为 `2260`
- 认证模式：Resin 必须设置 `RESIN_AUTH_VERSION=V1`，面板使用 V1 的 `Platform.Account:Token` 身份格式
- Proxy Token：保存到代理密码字段，仅用于数据面认证
- Platform：例如 `Default`；面板会为账号动态生成 `Platform.tg_account_<账号ID>` 身份
- 管理地址 / Admin Token：可选，用于校验控制面，并在账号切走时回收对应粘性租约

导入验证阶段会为每个条目生成独立临时身份；账号入库后，面板会通过 Resin 的
`inherit-lease` Action 将出口平滑继承给稳定账号身份。若 Resin 不支持该 Action，
或继承请求失败，账号仍会完成入库和代理绑定，但会保持停用，避免正式连接改用
未经确认的出口；升级 Resin 并重新绑定或重新登录后再启用账号。

Resin 提供的是“同账号优先保持出口”的租约，不承诺节点故障后 IP 永远不变。代理页和账号检测显示的是最近一次出口快照。

#### 默认后台账号（首次登录）

- 用户名：`tgpanel`
- 密码：`tgpanel123`

登录后建议尽快前往“修改密码”页面修改默认密码。

#### 常用命令

```bash
# 查看日志
docker compose logs -f

# 更新到当前 .env 指定的镜像版本
docker compose pull
docker compose up -d

# 重启 / 停止
docker compose restart
docker compose down
```

### 本地开发运行（可选）

需要 .NET 8 SDK。修改 Vue 前端后，先用仓库锁定的 pnpm 版本构建静态资源：

```bash
corepack enable
pnpm --dir frontend install --frozen-lockfile
pnpm --dir frontend run build
```

然后启动后端：

```bash
dotnet run --project src/TelegramPanel.Web
```

访问：`http://localhost:5000`

## Docker 一键更新（面板内）

面板已支持在 Docker 部署场景下一键更新（左上角版本号 → 版本信息弹窗）：

1. 点击“检查更新”，读取 GitHub 最新 Release。
2. 点击“一键更新并重启”，自动下载对应架构的 Linux 更新包到 `/data/app-current`。
3. 程序触发重启后，容器会优先从 `/data/app-current` 启动新版本（无需手动执行 `docker compose pull`）。

说明：

- 当前仅支持在 Docker 容器内部执行一键更新。
- 更新资产依赖 `release.yml` 工作流产物；若 Release 没有 `linux-x64` / `linux-arm64` 的 zip 资产，则一键更新会提示不可用。

升级前请备份 `docker-data`。旧版本数据恢复、镜像更新与回滚说明见[更新升级](docs/getting-started/update.md)。

## 文档入口

- [安装部署](docs/getting-started/installation.md)
- [账号导入](docs/guides/account-import.md)
- [代理管理与账号出口](docs/guides/proxy-management.md)
- [配置与数据目录](docs/reference/configuration.md)
- [同步说明](docs/guides/sync.md)
- [反向代理](docs/deployment/reverse-proxy.md)
- [模块开发](docs/developer/modules.md)
- [接口速查](docs/reference/api.md)

## 截图

更多截图见：`screenshot/`

| | | |
|---|---|---|
| <img src="screenshot/Dashboard.png" width="300" /> | <img src="screenshot/account.png" width="300" /> | <img src="screenshot/Import account.png" width="300" /> |

## ⭐ Star History

[![Star History Chart](https://api.star-history.com/svg?repos=moeacgx/Telegram-Panel&type=Date)](https://star-history.com/#moeacgx/Telegram-Panel&Date)
