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

## 功能概览

- 📥 **多账号导入 / 登录**：支持 Telethon / TData 压缩包导入导出、手机号验证码登录、2FA 密码处理
- 👥 **批量运营能力**：支持批量加群 / 订阅 / 退群 / 启动 BOT，在私密群组自动发送消息养号，批量邀请成员 / 机器人、批量设置管理员、导出链接等
- 📱 **一键踢出其他设备**：保留面板当前会话，清理其它在线设备
- 🧹 **废号检测与一键清理**：对封禁、受限、冻结、未登录、Session 失效等状态进行批量处理
- 🔐 **2FA 管理**：支持单个 / 批量修改二级密码，绑定 / 换绑找回邮箱（支持对接 Cloud Mail 自动收码确认）
- 👤 **账号可见性增强**：可在账号列表一键查看已加入的频道和群组，并展示注册时间（基于 777000 系统通知的估算值，非百分百准确）
- 🌐 **账号级代理管理**：支持 HTTP、SOCKS5、MTProxy、Resin 粘性网关，单账号 / 批量切换及导入时自动绑定
- ☁️ **WARP 独立出口**：Linux Docker 环境可为每个账号一键创建、检测并绑定独立 Cloudflare WARP 容器
- 🧩 **模块化扩展**：任务 / API 可安装扩展模块，支持 Vue 后台管理接口与旧 Razor 模块页面兼容（见 `docs/developer/modules.md`）

## 前端架构说明

主后台已经迁移为 Vue SPA，入口在 `/ui`。`.NET` 后端继续负责 API、后台任务、模块加载、旧 Razor 模块页面与兼容路由。

模块开发需要按下面的边界处理：

- 新模块优先提供 `/api/panel/extensions/{module-slug}` 管理接口，由 Vue 后台页面消费数据。
- 已有的 Razor 模块页面仍可通过 `IModuleUiProvider.GetPages` 注册，并由 `/ext/{moduleId}/{pageKey}` 兼容入口加载。
- 如果某个模块已经由宿主提供 Vue 原生页面，模块必须同步提供对应的管理接口，否则页面会出现接口 `404` 或回退到旧页面。
- 面向客户的公开分享页不要做成后台模块页，应通过 `MapEndpoints` 单独暴露匿名 token 页面或 API，并自行处理过期、权限隔离、防缓存和限流。

## 近期新增功能

- 🧠 **AI 验证接入**：持续活跃任务支持识别验证消息后自动点击按钮或文本作答
- ⚙️ **AI 设置增强**：支持 OpenAI 兼容端点、API Key、默认 / 预设模型以及一键连通测试
- 🔁 **AI 稳定性增强**：支持配置失败重试次数，AI 决策 / 作答 / 连通测试统一复用
- 📚 **数据字典能力**：支持文本字典、图片字典与模板变量
- 🕒 **定时任务能力**：新增定时频道 / 群组相关任务（创建、公开等）
- 🧠 **任务中心增强**：持续任务支持暂停、编辑、重新运行；任务列表区分执行中与历史任务；支持自动清理
- 💬 **持续活跃任务升级**：支持多分类账号、随机文案、秒级发送间隔、持续运行配置
- 🔄 **同步体验优化**：手动“立即同步”改为后台任务执行，可在任务中心跟踪进度
- 👤 **账号列表增强**：新增注册时间（估算）展示，并可查看账号已加入的频道 / 群组
- 📺 **频道管理升级**：频道列表改为“已加入频道”视角，支持多条件筛选与关联账号展示
- 👥 **群组管理补齐**：新增群组创建、分类、批量操作与列表能力
- 🔗 **多账号关系可视化**：频道 / 群组可绑定多个系统账号，列表与详情可查看关联状态
- 🚪 **真实退出 / 解散能力**：频道与群组支持单个 / 批量退出与解散
- 🧹 **数据准确性修正**：修复频道列表混入群组的问题，优化关系同步后的展示
- ♻️ **同步残留清理**：同步完成后自动清理失效关联与孤儿记录
- ⚡ **数据层优化**：补充查询与关系索引，提升大量账号 / 频道 / 群组场景下的筛选性能

## TODO（规划）

- [x] 一键退群 / 退订 / 订阅（频道 / 群组）
- [x] 批量自动签到
- [ ] 一键清空联系人
- [ ] 批量手机号验证码重新登录（用于刷新会话 Session）
- [ ] 手机号注册：未注册号码支持完整注册流程（姓名 / 可选邮箱 / 邮箱验证码等）
- [ ] 通用接码 API：抽象接口 + 主程序只依赖抽象；厂商通过“适配模块”对接（无需改动主程序代码）
- [ ] 支持更换手机号
- [x] 多代理：代理池、账号级 / 批量绑定、Resin 粘性路由、导入自动 WARP
- [ ] 多 API：支持账号分类绑定 ApiId / ApiHash
- [ ] 定时创建频道、定时公开频道
- [ ] 定时刷粉丝：对接刷粉 API（通用适配结构），通过适配模块对接多家刷粉平台
- [x] 群聊定时发言养号

## 快速开始

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
```

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

用户名：`tgpanel`

密码：`tgpanel123`

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

> 适合需要改代码或本地调试的场景（需先安装 .NET 8 SDK）。

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

## 截图

更多截图见：`screenshot/`

| | | |
|---|---|---|
| <img src="screenshot/Dashboard.png" width="300" /> | <img src="screenshot/account.png" width="300" /> | <img src="screenshot/Import account.png" width="300" /> |

## ⭐ Star History

[![Star History Chart](https://api.star-history.com/svg?repos=moeacgx/Telegram-Panel&type=Date)](https://star-history.com/#moeacgx/Telegram-Panel&Date)
