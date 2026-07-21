# 管理接口速查

Vue 后台使用 `/api/panel` 下的管理接口。开启后台登录时，除登录等少数端点外都需要
管理员 Cookie；这些接口不是面向公网的稳定开放 API。完整行为以
`PanelAdminApiEndpoints.cs` 和各功能 Endpoint 文件为准。

## 登录与账号

- `POST /api/panel/auth/login`：后台登录
- `GET /api/panel/auth/me`：当前后台登录状态
- `GET /api/panel/accounts`：账号列表
- `GET /api/panel/accounts/{id}`：账号详情
- `POST /api/panel/accounts/import/zip`：导入 Telethon 或 TData 压缩包
- `POST /api/panel/accounts/import/session-files`：导入 Session 文件
- `POST /api/panel/accounts/import/string-session`：导入 StringSession
- `POST /api/panel/accounts/login/start`：开始手机号登录
- `POST /api/panel/accounts/login/qr/start`：开始二维码登录
- `POST /api/panel/accounts/login/code`：提交手机号验证码
- `POST /api/panel/accounts/login/password`：提交 2FA 密码
- `DELETE /api/panel/accounts/{id}`：删除账号

前端会为登录和导入请求明确携带 `proxyStrategy`；自定义调用也应显式传入。兼容旧调用时，
服务端会把省略策略解释为“使用全局代理”，全局代理无效则在连接 Telegram 前失败，不会
回退直连。不要绕过这些入口自行先直连创建 Session。

## 代理与出口

- `GET /api/panel/network/egress`：检测面板服务自身出口
- `GET /api/panel/settings/global-proxy`：读取账号全局代理配置；密码与 Secret 仅返回是否已设置
- `POST /api/panel/settings/global-proxy`：启用、修改或关闭账号全局代理并清理客户端缓存
- `GET /api/panel/proxies`：代理列表
- `GET /api/panel/proxies?usage=used|unused&categoryId={id}`：按使用状态或分类筛选代理
- `GET/POST/PUT/DELETE /api/panel/proxy-categories[/{id}]`：查询和管理代理分类
- `POST /api/panel/proxies/batch/category`：批量设置代理分类
- `POST /api/panel/proxies`：新增普通代理或 Resin
- `PUT /api/panel/proxies/{id}`：修改代理
- `POST /api/panel/proxies/{id}/test`：检测代理出口
- `GET /api/panel/proxies/warp/status`：受管 WARP 运行环境
- `POST /api/panel/proxies/warp`：创建受管 WARP
- `POST /api/panel/proxies/{id}/warp/refresh`：重启并复测单个受管 WARP
- `POST /api/panel/proxies/warp/refresh-all`：依次重启并复测全部期望启用的 WARP
- `POST /api/panel/accounts/{id}/proxy`：切换单个账号路由
- `POST /api/panel/accounts/batch/proxy`：批量切换账号路由
- `GET /api/panel/accounts/{id}/proxy/egress`：检测账号实际出口

`POST /api/panel/settings/global-proxy` 使用 `sourceMode=manual|existing`。`existing` 模式
必须提供 `proxyId`，服务端只保存引用并在运行时解析代理；不会把 WARP 或 Resin 的连接
凭据复制到全局配置。

## 频道、群组和 Bot

- `GET /api/panel/channels` / `GET /api/panel/groups`：列表和筛选
- `GET /api/panel/channels/{id}` / `GET /api/panel/groups/{id}`：详情
- `POST /api/panel/channels` / `POST /api/panel/groups`：创建
- `GET /api/panel/bots`：Bot 列表
- `GET /api/panel/bot-channels`：Bot 频道列表

批量邀请、管理员变更、退出和解散等端点可在对应 Vue API 调用或 Endpoint 文件中查看。

## 任务和模块

- `GET /api/panel/tasks`：任务列表
- `POST /api/panel/tasks`：创建任务
- `POST /api/panel/tasks/{id}/pause`：暂停
- `POST /api/panel/tasks/{id}/resume`：恢复
- `POST /api/panel/tasks/{id}/cancel`：取消
- `DELETE /api/panel/tasks/{id}`：删除
- `GET /api/panel/modules`：模块列表
- `POST /api/panel/modules/install`：安装模块包
- `/api/panel/extensions/{module-slug}`：模块自定义后台管理接口约定

需要给外部系统调用时，优先使用模块的 `MapEndpoints` 明确设计鉴权、限流和响应模型，
不要直接把管理 Cookie 接口暴露到公网。
