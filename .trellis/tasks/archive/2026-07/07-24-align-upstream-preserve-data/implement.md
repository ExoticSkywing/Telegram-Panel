# 执行计划：保留核心数据并对齐上游最新版

## Gate A：执行前基线与备份

- [x] 记录 UTC 时间戳、当前 Git HEAD、分支、两个自有远端引用、容器 ID、镜像 ID、Compose 最终配置和运行目录。
- [x] 创建仓库外、权限为 `0700` 的迁移备份目录。
- [x] 记录数据库 `PRAGMA quick_check`、业务表记录数、数据库文件/WAL 状态和 Session 文件数量。
- [x] 停止 `telegram-panel`，确认进程不再写入 `/data`。
- [x] 完整复制 `docker-data/`、`.env`、当前 Compose、Trellis 路径和本地 `AGENTS.md` 到迁移备份。
- [x] 通过 SQLite `.backup` 创建第二份数据库备份，并对备份执行 `quick_check`。
- [x] 为当前容器镜像创建带时间戳的本地回滚标签，记录到备份清单。
- [x] 首次文件对比因 SQLite 关闭时清理空的 WAL/SHM 而中止，保护逻辑成功重启旧容器；第二次稳定快照通过全部门禁后才继续迁移。

## Gate B：本地 Git 对齐

- [x] 添加或校正 `upstream` 远端并拉取 `upstream/main`。
- [x] 再次确认 `origin/main`、`team/main` 的远端 SHA 并记录；本任务不得执行 push。
- [x] 将本地 `main` 重置到 `upstream/main`。
- [x] 运行 `git clean -fd` 清除非忽略的旧应用残留；未使用 `-x`。
- [x] 从仓库外备份恢复 `.trellis/`、`.agents/`、`.codex/`。
- [x] 将 Trellis 托管块合并进官方 `AGENTS.md`，确保官方规范完整且托管块只出现一次。
- [x] 验证 `.env`、`docker-data/` 未丢失且仍被 Git 忽略。
- [x] 审计相对 `upstream/main` 的路径差异，确认没有旧业务源码或旧官方部署文件残留。

## Gate C：官方镜像部署

- [x] 在 `.env` 中设置官方 `TP_IMAGE` 和 `TP_UPDATE_MODE=image`，保留其他现有部署值。
- [x] 创建被忽略的 `docker-compose.override.yml`，把端口唯一映射为 `127.0.0.1:5232:5000`。
- [x] 运行 `docker compose config`，检查镜像、更新模式、数据挂载和唯一端口映射；输出展示时脱敏。
- [x] 拉取 `ghcr.io/moeacgx/telegram-panel:latest`，记录远端/本地镜像标识。
- [x] 强制重建 `telegram-panel`，等待健康检查；未启动本地源码 build。

## Gate D：迁移后验证

- [x] 确认容器镜像为新拉取的官方镜像、PID 1 工作目录为 `/app`、更新模式为 `image`；首次安全退出后重启次数稳定为 1。
- [x] 验证 `curl http://127.0.0.1:5232/healthz` 成功。
- [x] 验证 `/api/panel/auth/me`、`/ui/dashboard` 和认证拦截行为，并检查启动日志中的存储路径与数据库迁移结果。
- [x] 对升级后数据库执行 `PRAGMA quick_check`。
- [x] 比较升级前后关键表记录数，原有表均未减少。
- [x] 验证停机前基线的 12 个账号、17 个 Session 文件、后台凭据、配置和其他持久文件仍存在。
- [x] 运行 Trellis 上下文脚本，确认任务、工作区和技能仍可读。
- [x] 复核 `origin/main`、`team/main` 仍为迁移前记录的 SHA，且没有任何 push。

## Gate E：失败回滚或成功收尾

- [x] 未触发关键失败；旧数据快照和旧镜像回滚标签均已保留，可按设计执行双重回滚。
- [x] 成功时把“上游应用基线 + Trellis 管理层”作为本地提交记录；不推送任何远端。
- [x] 按 Trellis 质量检查与 finish-work 流程记录验证结果、回滚点和仍需人工验证的后台登录。

## 执行证据

- 迁移时间：2026-07-24 UTC。
- 数据备份：`/root/backups/telegram-panel/20260724T062859Z`，目录权限 `0700`，稳定快照与独立 SQLite `.backup` 的 `quick_check` 均为 `ok`。
- Git 基线：本地应用基线 `upstream/main=0bc281682ee995937c74fb8f38e5cb0cb866fd00`；`origin/main` 与 `team/main` 均保持 `63d9af66c42bacfe9194069074c7724c4ddd0300`。
- 镜像：旧镜像 `sha256:bf594d20...` 标记为 `telegram-panel:rollback-20260724T062859Z`；新镜像为 `sha256:e57d846f...`。
- 首次新版本启动检测到数据库在检查期间变化，按上游保护逻辑以退出码 0 安全重启；第二次启动成功写入持久迁移标记，随后重启次数稳定为 1，未再出现错误。
- 数据验证：SQLite `quick_check=ok`，迁移记录 `17 -> 22`，非内部表 `17 -> 20`，账号 `12 -> 12`，所有原有表记录数均未减少，Session 文件集合保持 17 个。
- 配置验证：`admin_auth.json` 与 `appsettings.local.json` 和升级前快照逐字一致；`/app`、`image` 模式、`127.0.0.1:5232 -> 5000` 均符合设计。
- HTTP 验证：`/healthz=200`、`/ui/dashboard=200`、`/api/panel/auth/me=200`，认证启用且版本为 `1.31.38`。
- `.env` 的首次初始化密码不匹配现有持久凭据（登录尝试返回 401），符合密码曾被修改的状态；没有重置凭据，用户应继续使用当前后台密码。

## 风险点与停止条件

- 数据备份不完整或 SQLite 检查失败：停止。
- `git diff upstream/main` 出现旧业务源码：停止并清理来源，不进入部署。
- Compose 同时发布 `5000` 和 `5232`：停止，修正 override 后再继续。
- 新容器发生重启循环、认证接口 5xx、数据数量减少或 Session 缺失：立即执行旧镜像 + 旧数据双重回滚。
- 不得执行 `git push`、`git clean -fdx`、删除备份目录或清理旧镜像。
