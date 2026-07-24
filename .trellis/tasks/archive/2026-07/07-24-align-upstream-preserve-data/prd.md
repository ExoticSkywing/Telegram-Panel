# 保留核心数据并对齐上游最新版

## Goal

在不丢失 Telegram Panel 现有业务数据和登录状态的前提下，使生产程序固定运行官方上游最新镜像，并使本地应用源码与 `moeacgx/Telegram-Panel` 的 `main` 分支对齐；保留本仓库的 Trellis 工作流能力和历史记录。

## Background

- 当前本地 `main` 为 `63d9af6`，官方上游 `main` 为 `0bc2816`，现有业务源码改动不再保留。
- 当前容器使用 `ghcr.io/moeacgx/telegram-panel:latest`，但本机镜像旧于远端 `latest`；容器实际从 `/app` 运行，`/data/app-current` 不存在。
- 核心状态通过 `./docker-data:/data` 持久化；停机前最终基线的 SQLite `quick_check` 通过，共 17 张非 SQLite 内部表、12 个账号，并有 17 个 Session 文件。
- 官方最新版继续使用 `/data/telegram-panel.db`、`/data/sessions`、`/data/appsettings.local.json`、`/data/admin_auth.json` 等持久路径，并在启动时处理数据库迁移。
- 官方上游只包含自己的 `AGENTS.md`，不包含本地 `.trellis/`、`.agents/`、`.codex/`。本地 `AGENTS.md` 目前只有 Trellis 托管块，因此对齐时需要把官方开发规范和 Trellis 托管块合并，不能二选一覆盖。

## Requirements

- R1：切换前停止应用并把整个 `docker-data/` 备份到仓库之外，同时保留可用于回滚的旧镜像和部署配置。
- R2：丢弃现有业务源码修改，使应用源码来自官方上游 `main`；不得把旧业务改动重新移植到上游。
- R3：保留 `.trellis/`、`.agents/`、`.codex/` 及 Trellis 管理的 `AGENTS.md` 块，包括本次任务与既有工作记录。
- R4：`AGENTS.md` 同时保留官方上游开发规范和 Trellis 托管块。
- R5：生产程序使用 `ghcr.io/moeacgx/telegram-panel:latest`，并使用 `image` 更新模式，防止 `/data/app-current` 覆盖镜像程序。
- R6：继续使用宿主机 `127.0.0.1:5232` 转发容器 `5000`，避免破坏现有宝塔反向代理；端口差异通过 Git 忽略的 Compose override 表达，不修改官方 Compose 文件。
- R7：`.env`、数据库、Session、后台凭据、面板设置及上传文件不进入 Git，也不因源码清理被删除。
- R8：本次不得覆盖或推送 `team/main`。
- R9：数据库迁移后必须验证健康状态、运行来源、登录接口、业务数据数量和 Session 文件数量；失败时恢复旧镜像及升级前数据快照。
- R10：本次不得覆盖或推送 `origin/main`；个人仓库与团队仓库均继续保留旧历史作为迁移保险。

## Acceptance Criteria

- [x] AC1：仓库外存在带时间戳的完整数据备份，并通过文件清单、SQLite 完整性检查和关键数量基线验证。
- [x] AC2：本地应用源码与官方上游 `main` 对齐；与上游的剩余差异仅限 Trellis 集成/记录和明确的本地部署覆盖，不包含旧业务功能修改。
- [x] AC3：`.trellis/`、`.agents/`、`.codex/` 可用，当前任务及既有 Trellis 记录仍存在。
- [x] AC4：`AGENTS.md` 同时包含官方规范与唯一一份完整 Trellis 托管块。
- [x] AC5：容器使用拉取后的官方 `latest` 镜像、工作目录为 `/app`、更新模式为 `image`，且健康检查通过。
- [x] AC6：`127.0.0.1:5232/healthz` 返回成功，现有反向代理目标无需修改，容器业务端口不直接暴露到公网地址。
- [x] AC7：升级后的数据库完整性通过，关键表记录数量符合迁移预期，停机前基线的 12 个账号与 17 个 Session 文件仍在，后台认证加载了未变化的持久凭据。
- [x] AC8：`team/main` 引用和远端内容未被修改或推送。
- [x] AC9：已记录可执行的旧镜像与数据双重回滚步骤。
- [x] AC10：`origin/main` 引用和远端内容未被修改或推送。

## Out of Scope

- 不把已经放弃的本地业务修复移植到上游新版。
- 不覆盖或强制推送 `team/main`。
- 不覆盖或强制推送 `origin/main`。
- 不在本次操作中修改 DNS、OCI 安全列表或宝塔反向代理目标。
- 不启用 WARP 或其他上游新增的可选部署组件。
