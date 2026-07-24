# 技术设计：保留核心数据并对齐上游最新版

## 1. 边界与最终形态

迁移后仓库由两部分组成：

1. **官方应用层**：以 `upstream/main` 为唯一来源，旧业务源码、旧 Compose 修改和旧功能修复全部放弃。
2. **本地管理层**：在官方提交之上保留 `.trellis/`、`.agents/`、`.codex/`，并在官方 `AGENTS.md` 中加入一份 Trellis 托管块。

运行程序不从本地源码构建，而固定使用 `ghcr.io/moeacgx/telegram-panel:latest`。部署差异只存在于 Git 忽略文件：`.env`、`docker-compose.override.yml` 和 `docker-data/`。

`origin/main`、`team/main` 均保持当前远端状态，本次只重写本地 `main` 的基线，不执行任何 push。

## 2. 数据与程序边界

```text
upstream/main + Trellis 元数据
              │
              │ docker compose pull / recreate
              ▼
ghcr.io/moeacgx/telegram-panel:latest  ──运行──> /app
              │
              │ bind mount
              ▼
./docker-data  ──>  /data
  ├─ telegram-panel.db
  ├─ sessions/
  ├─ admin_auth.json
  ├─ appsettings.local.json
  ├─ uploads/
  └─ 其他上游生成的持久文件
```

升级只替换程序层。整个 `docker-data/` 作为一个不可拆分的恢复单元备份和恢复，避免遗漏 WAL、迁移标记、上传文件、模块状态或未来新增的数据。

## 3. Git 对齐策略

### 3.1 上游引用

- 新增或校正只读语义的远端 `upstream=https://github.com/moeacgx/Telegram-Panel.git`。
- 拉取 `upstream/main` 后，将本地 `main` 重置到其准确提交。
- 清理只允许使用 `git clean -fd`；禁止 `git clean -fdx`，因为后者会删除 `.env` 和 `docker-data/`。

### 3.2 Trellis 保存与恢复

重置前把以下路径复制到仓库外的迁移备份：

- `.trellis/`
- `.agents/`
- `.codex/`
- 当前本地 `AGENTS.md`

清理完成后恢复前三个目录。`AGENTS.md` 不直接覆盖：以官方文件为主体，再插入唯一一份 `<!-- TRELLIS:START --> ... <!-- TRELLIS:END -->` 托管块。这样官方开发规范与 Trellis 路由同时生效。

恢复后使用路径差异审计确认：相对 `upstream/main` 的非运行时差异只能属于上述 Trellis 路径和合并后的 `AGENTS.md`，不得出现旧 `src/`、`frontend/`、Dockerfile 或官方 Compose 差异。

## 4. 数据备份与一致性

### 4.1 停机快照

先记录容器、镜像、Git、Compose 和数据库基线，再停止 `telegram-panel`。容器停止后：

- 使用 `cp -a` 在仓库外创建完整 `docker-data` 副本；
- 使用 SQLite `.backup` 额外生成独立数据库备份；
- 保存 `.env`、旧 Compose、旧 Git HEAD 和旧镜像标识；
- 给旧镜像增加仅本机使用的回滚标签；
- 对备份数据库执行 `PRAGMA quick_check`；
- 保存各业务表记录数与 Session 文件数量。

备份目录权限限制为仅 root 可访问，因为其中包含 Session、后台凭据和 Webhook 密钥。

### 4.2 数据库迁移

上游应用启动时对既有 SQLite 执行 EF Core 迁移。迁移是可能不可逆的数据修改，因此回滚必须同时恢复旧镜像和升级前数据库快照；不允许只切回旧镜像继续读取已升级数据库。

## 5. 部署配置

### 5.1 镜像来源

`.env` 保证：

```dotenv
TP_IMAGE=ghcr.io/moeacgx/telegram-panel:latest
TP_UPDATE_MODE=image
```

`image` 模式强制入口运行 `/app`，避免以后面板一键更新产生的 `/data/app-current` 遮蔽官方镜像。

### 5.2 端口兼容

官方 Compose 发布 `5000:5000`，现有宝塔反代指向 `127.0.0.1:5232`。使用 Compose v5.3.0 支持的 `!override` 标签覆盖官方端口：

```yaml
services:
  telegram-panel:
    ports: !override
      - "127.0.0.1:5232:5000"
```

该文件名为 `docker-compose.override.yml`，已被官方 `.gitignore` 忽略。绑定到 loopback 还可避免应用端口直接暴露公网。

## 6. 验证策略

### 6.1 静态验证

- `git rev-parse upstream/main` 与新的应用基线一致；
- `git diff upstream/main` 只包含 Trellis 管理层；
- `docker compose config` 成功，最终端口只有 `127.0.0.1:5232 -> 5000`；
- 最终镜像解析为官方 GHCR `latest`，更新模式解析为 `image`。

### 6.2 运行验证

- 容器进入 `healthy` 且无重启循环；
- `/healthz` 通过；
- `/api/panel/auth/me` 返回符合未登录或已登录状态的预期响应，而非 5xx；
- PID 1 工作目录为 `/app`；
- 容器镜像 ID 对应拉取后的远端 `latest`；
- SQLite `quick_check` 为 `ok`；
- 原有表记录数没有非迁移预期的减少，停机前基线的 12 个账号与 17 个 Session 仍存在；
- 后台实际登录可用需要用户侧凭据验证，自动检查只验证认证接口和凭据文件仍被加载。

## 7. 回滚

若容器不健康、认证接口 5xx、数据库完整性失败或关键数据减少：

1. 停止并移走失败后的 `docker-data`，不得覆盖以便保留诊断证据。
2. 从仓库外快照恢复完整旧 `docker-data` 和旧 `.env`。
3. 使用迁移前创建的本地旧镜像标签，以 `--pull never` 重建容器。
4. 重新执行健康、数据库完整性和数量基线检查。
5. Git 源码是否回到旧历史与服务回滚相互独立；旧历史仍存在于 `origin/main`、`team/main` 和备份记录中。

## 8. 取舍

- 本地 `main` 不会逐字等于上游提交，因为其上保留了 Trellis 管理层；应用运行代码仍与上游一致。
- 两个自有远端暂时保留旧历史会造成本地分支显示分叉，但提供额外恢复点且不影响官方镜像运行。
- 保留既有 Trellis spec 与历史任务，但上游结构已显著变化；本次只验证 Trellis 工具可运行，不在迁移任务中全面重写所有编码规范。
