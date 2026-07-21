# 代理管理与账号出口

Telegram Panel 按账号管理 Telegram 连接出口。导入、登录，以及后台任务和模块对账号
执行的 Telegram 操作都会复用账号当前路由，避免同一个账号先直连、后切换代理。

## 先区分面板出口和账号出口

代理管理页顶部显示的是**面板服务自身的公网出口**。代理列表和账号详情显示的是
对应代理或账号的出口，两者互不等价。

- 顶部显示“未使用 WARP”：只表示面板服务自身没有通过 Cloudflare WARP。
- WARP 代理行显示“WARP 已连接”：表示该独立 WARP 容器的出口检测成功。
- 出口地址包含冒号时通常是 IPv6。IPv6 同样是有效公网出口。
- 当前出口检测使用 Cloudflare Trace，稳定返回 IP、国家码和 WARP 状态；城市和运营商
  可能为空，这不代表检测失败。

## 选择账号使用的出口

账号支持以下路由：

- **明确直连**：绕过账号代理和全局代理。
- **全局代理**：继承 `Telegram:Proxy` 配置。
- **已有代理**：绑定代理管理中的 HTTP、SOCKS5、MTProxy 或 Resin。
- **独立 WARP**：为账号创建并绑定受管 WARP 容器。

导入账号、手机号登录和二维码登录都会在第一条 Telegram 请求前要求选择路由。
选定后，验证码发送、二维码轮询、2FA 验证和 Session 建立会使用同一出口；失败时不会
静默回退到面板直连。

切换已入库账号的代理时，宿主会先停用账号并严格断开旧客户端，再提交新路由并重建
连接。模块和后台任务下一次按账号执行操作时会自动使用新出口。

## 添加和检测普通代理

在 **代理管理** 中添加代理，然后执行出口检测。支持：

- HTTP
- SOCKS5
- MTProxy
- Resin HTTP 或 SOCKS5 数据面

HTTP 和 SOCKS5 可以通过 Cloudflare Trace 检测公网出口。MTProxy 只服务 Telegram
MTProto，不能通过普通 HTTP 请求检测公网 IP。

## 启用独立 WARP

普通代理和 Resin 不需要 Docker Socket。只有需要面板创建独立 WARP 容器时，才叠加
受管 WARP 配置：

```bash
docker compose -f docker-compose.yml -f docker-compose.warp.yml up -d
```

`docker-compose.warp.yml` 会把 `/var/run/docker.sock` 挂入面板容器。该权限接近宿主机
`root`，只应在可信主机启用。

可以在 `.env` 设置：

```dotenv
# Compose 项目名不是 telegram-panel 时，改为面板所在的实际 Docker 网络
TP_WARP_DOCKER_NETWORK=telegram-panel_default

# 自动创建 WARP 的默认连接协议：http 或 socks5
TP_WARP_PROXY_PROTOCOL=http
```

WARP 镜像中的 GOST 端口同时支持 HTTP 和 SOCKS5。默认协议决定登录、导入和批量绑定
自动创建 WARP 时宿主使用哪种握手；代理管理中的一键创建弹窗可以覆盖单次创建协议。

默认 `container` 模式由 Docker 网络按容器名访问，不占用宿主机代理端口。若在其他
拓扑中把 `Proxy:Warp:ProxyHostMode` 配为 `published`，面板会从
`Proxy:Warp:HostPortStart`（默认 `42080`）开始递增寻找空闲端口。若检测通过后端口又在
Docker 创建或启动时被抢占，面板会删除失败的容器壳、保留数据卷，并继续尝试下一端口。

## 自动巡检与故障恢复

Docker 的 `unless-stopped` 只能处理容器进程退出，不能处理“容器仍显示 running，
但 WARP 隧道或 GOST 已经卡死”。面板因此还会执行出口级自动维护：

- 默认每 5 分钟检测所有期望启用的受管 WARP。
- 连续失败 2 次后重启原容器，保留 WARP 数据卷，并最多复测 6 次。
- 恢复失败后进入 30 分钟冷却，避免检测源抖动造成重启风暴。
- 重启前后释放绑定账号的 Telegram 客户端；客户端只能沿原 WARP 路由重建，代理不可用时
  会失败，不会回退为面板直连。
- 正在用于账号导入、手机号登录或二维码登录的 WARP（包括已有 WARP 和一键新建 WARP）
  会保持首次出口冻结；后台巡检、手动刷新、修改和删除都不会打断首次连接。
- 代理页每 30 秒更新维护状态，也可手动刷新单个或全部 WARP。

参考 tokens-pro 的“720 分钟定时刷新”也可以开启：

```dotenv
TP_WARP_AUTO_RECOVERY_ENABLED=true
TP_WARP_HEALTH_CHECK_INTERVAL_MINUTES=5
TP_WARP_FAILURE_THRESHOLD=2
TP_WARP_RECOVERY_COOLDOWN_MINUTES=30
TP_WARP_SCHEDULED_REFRESH_ENABLED=false
TP_WARP_SCHEDULED_REFRESH_INTERVAL_MINUTES=720
```

故障自愈默认开启；健康出口的定时强制重启默认关闭，因为重启可能更换账号出口 IP。
只有确实需要周期轮换时才把 `TP_WARP_SCHEDULED_REFRESH_ENABLED` 改为 `true`。

参考项目界面中的 `WARP_SLEEP=2` 是 WARP 镜像内部启动等待参数，`GOST_ARGS=-L :1080`
是代理监听参数；它们本身都不等于定时健康巡检。

修改 `.env` 后重新创建面板容器：

```bash
docker compose -f docker-compose.yml -f docker-compose.warp.yml up -d --force-recreate
```

## 对接 Resin 动态代理

先按 [Resin 中文文档](https://github.com/Resinat/Resin/blob/master/README.zh-CN.md)
部署网关，再在 **代理管理** 中新增 `Resin`：

- 主机和端口：Resin HTTP 或 SOCKS5 数据面地址。
- Proxy Token：保存到代理密码字段，只用于数据面认证。
- Platform：例如 `Default`。
- 管理地址和 Admin Token：用于检查控制面并回收粘性租约。

面板会为账号生成稳定身份。导入阶段使用临时身份验证出口，入库后通过
`inherit-lease` 把租约继承给正式账号身份。继承失败时账号会保持停用，避免正式连接
改用未经确认的出口。

Resin 提供粘性租约，但不保证节点故障后 IP 永远不变。页面展示的是最近一次成功检测
得到的出口快照。

## 模块不重复管理账号代理

模块对已入库账号执行 Telegram 操作时，应把 `accountId` 交给宿主账号服务。宿主客户端池
会自动解析账号路由并应用代理，模块不应再保存代理凭据或自行创建 `WTelegram.Client`。

模块自己的 `HttpClient`、第三方 API 或其它网络连接不会自动继承账号代理。如果这类请求
确实需要代理，应作为模块自己的独立网络能力设计。完整边界见
[模块开发文档](../developer/modules.md)。
